using System.Net;
using System.Text;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// A whole service, hosted in-process, that is either a product service or a domain service.
/// </summary>
/// <remarks>
/// The same service both ways (two types differing only in <see cref="ProductServiceAttribute"/>,
/// since an attribute cannot be switched at run time), so that a test comparing the two is
/// comparing the opt-in and nothing else. Everything between the request and the handler is the
/// platform's own: authentication, the refusal, authorization, binding and validation. Only the far
/// side of a downstream call is a double, which answers every call.
/// </remarks>
public sealed class SessionHost : IAsyncDisposable
{
    /// <summary>
    /// The scope every probe operation demands.
    /// </summary>
    public const string Scope = "probes.read";

    /// <summary>
    /// The scope a probe write demands.
    /// </summary>
    public const string WriteScope = "probes.write";

    /// <summary>
    /// The scope an organization credential must hold for the probes that accept one.
    /// </summary>
    public const string OrganizationCredentialScope = "probes.organization";

    /// <summary>
    /// The scope a Send credential carries, for the probe that accepts one.
    /// </summary>
    public const string SendScope = "probes.send";

    /// <summary>
    /// A second scope that also admits the <c>any-scope</c> probe.
    /// </summary>
    public const string OtherScope = "probes.other";

    /// <summary>
    /// The service's registered name, and so its credential's.
    /// </summary>
    public const string ServiceName = "session-test-service";

    /// <summary>
    /// The service a probe calls on its way through.
    /// </summary>
    public const string Downstream = "downstream-service";

    /// <summary>
    /// The issuer of every token the host signs.
    /// </summary>
    public const string Issuer = "https://identity.session.test";

    /// <summary>
    /// The setting that has the host bind its bearer scheme from configuration, as a deployed service
    /// does, and take the issuer and keys from a discovery document instead of naming them.
    /// </summary>
    public const string BearerAuthority = "Authentication:Schemes:Bearer:Authority";

    private static readonly SymmetricSecurityKey Key =
        new(Encoding.UTF8.GetBytes("session-host-test-signing-key-signs-nothing-real"));

    private readonly WebApplication _app;

    private SessionHost(WebApplication app)
    {
        _app = app;
    }

    /// <summary>
    /// A client presenting nothing; a test adds the token and headers it means.
    /// </summary>
    public HttpClient Client() => _app.GetTestClient();

    /// <summary>
    /// Which service the host says it is.
    /// </summary>
    public IServiceIdentity Identity => _app.Services.GetRequiredService<IServiceIdentity>();

    /// <summary>
    /// One of the running service's registrations.
    /// </summary>
    public T Resolve<T>() where T : notnull => _app.Services.GetRequiredService<T>();

    /// <summary>
    /// Starts the service.
    /// </summary>
    /// <param name="productService">Whether it is marked <see cref="ProductServiceAttribute"/>.</param>
    /// <param name="configuration">Settings beyond the host's own, as a deployment would add them.</param>
    /// <param name="services">
    /// Registrations made after the service's own, as a service's <c>ConfigureServices</c> would
    /// make them: a test double replacing a platform default, say.
    /// </param>
    public static async Task<SessionHost> StartAsync(
        bool productService,
        IReadOnlyDictionary<string, string?>? configuration = null,
        Action<IServiceCollection>? services = null)
    {
        var settings = configuration ?? new Dictionary<string, string?>();
        ProbeService service = productService
            ? new ProductProbeService(settings, services)
            : new DomainProbeService(settings, services);
        var app = service.Build();
        await app.StartAsync(TestContext.Current.CancellationToken);

        return new SessionHost(app);
    }

    /// <summary>
    /// Starts a service derived from a product service that does not repeat the attribute.
    /// </summary>
    public static async Task<SessionHost> StartDerivedFromProductServiceAsync()
    {
        var app = new DerivedProductProbeService(new Dictionary<string, string?>()).Build();
        await app.StartAsync(TestContext.Current.CancellationToken);

        return new SessionHost(app);
    }

    /// <summary>
    /// A signed session token, as Identity would issue one.
    /// </summary>
    /// <param name="user">The person it names as <c>sub</c>, or null for a token naming nobody.</param>
    /// <param name="scopes">What it grants.</param>
    public static string Token(Guid? user, params string[] scopes) =>
        Token(user, new Dictionary<string, object>(), scopes);

    /// <summary>
    /// The audiences a token names unless a test says otherwise: this host's own name, which either
    /// kind of host accepts, so a test about something else is not also a test about the audience.
    /// </summary>
    public static readonly string[] EitherAudience = [ServiceName];

    /// <summary>
    /// A signed session token carrying claims beyond the usual ones.
    /// </summary>
    /// <param name="user">The person it names as <c>sub</c>, or null for a token naming nobody.</param>
    /// <param name="extra">Further claims, such as the <c>device</c> a session was issued to.</param>
    /// <param name="scopes">What it grants.</param>
    public static string Token(Guid? user, IReadOnlyDictionary<string, object> extra, params string[] scopes) =>
        AddressedToken(EitherAudience, user, extra, scopes);

    /// <summary>
    /// A signed session token naming exactly these audiences as <c>aud</c>.
    /// </summary>
    /// <param name="audiences">Whom it is addressed to; none leaves <c>aud</c> out.</param>
    /// <param name="user">The person it names as <c>sub</c>, or null for a token naming nobody.</param>
    /// <param name="extra">Further claims.</param>
    /// <param name="scopes">What it grants.</param>
    public static string AddressedToken(
        IReadOnlyCollection<string> audiences,
        Guid? user,
        IReadOnlyDictionary<string, object> extra,
        params string[] scopes)
    {
        var claims = new Dictionary<string, object>(extra)
        {
            ["client_id"] = user is null ? "internal.some-service" : "web",
            ["scope"] = scopes,
        };

        if (audiences.Count > 0)
        {
            claims["aud"] = audiences.ToArray();
        }

        if (user is not null)
        {
            claims["sub"] = user.Value.ToString();
        }

        var now = DateTime.UtcNow;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            TokenType = "at+jwt",
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
            Claims = claims,
        });
    }

    /// <summary>
    /// A context token, as Identity's exchange signs one for a call to this host.
    /// </summary>
    /// <param name="client">The calling service it is bound to.</param>
    /// <param name="organization">The organization the call concerns.</param>
    /// <param name="user">The user it is made on behalf of.</param>
    /// <param name="provider">The provider on whose authority it is made.</param>
    /// <param name="context">The context object, as its header carries it.</param>
    /// <param name="audience">The callee; this host unless a test says otherwise.</param>
    /// <param name="type">Its JOSE type.</param>
    /// <param name="lifetime">How long it lives; negative for one already expired.</param>
    /// <param name="mode">Delegated, as a product service's call for a person, unless a test says otherwise.</param>
    /// <param name="hops">How many services the call passed through: its <c>act</c> chain's length.</param>
    /// <param name="claims">Claims replacing the usual ones; a null value leaves that claim out.</param>
    /// <param name="issuer">Who it says issued it; this host's issuer unless a test says otherwise.</param>
    /// <param name="expires">Whether it carries <c>exp</c> at all.</param>
    /// <param name="key">The key it is signed with; this host's unless a test says otherwise.</param>
    public static string SignedContext(
        string client = "internal.some-service",
        Guid? organization = null,
        Guid? user = null,
        Guid? provider = null,
        string context = """{"v":1}""",
        string audience = ServiceName,
        string type = CallerContextToken.Type,
        TimeSpan? lifetime = null,
        string mode = CallerContextToken.Delegated,
        int hops = 1,
        IReadOnlyDictionary<string, object?>? claims = null,
        string issuer = Issuer,
        bool expires = true,
        SecurityKey? key = null)
    {
        object actor = new Dictionary<string, object> { ["sub"] = "internal.first-service" };
        for (var hop = 1; hop < hops; hop++)
        {
            actor = new Dictionary<string, object> { ["sub"] = $"internal.service-{hop}", [CallerContextToken.ActorClaim] = actor };
        }

        var payload = new Dictionary<string, object>
        {
            [CallerContextToken.ClientClaim] = client,
            [CallerContextToken.ModeClaim] = mode,
            [CallerContextToken.ActorClaim] = actor,
            [CallerContextToken.ContextClaim] = context,
        };

        if (organization is not null)
        {
            payload[CallerContextToken.OrganizationClaim] = organization.Value.ToString();
        }

        if (user is not null)
        {
            payload[CallerContextToken.UserClaim] = user.Value.ToString();
        }

        if (provider is not null)
        {
            payload[CallerContextToken.ProviderClaim] = provider.Value.ToString();
        }

        foreach (var (name, value) in claims ?? new Dictionary<string, object?>())
        {
            if (value is null)
            {
                payload.Remove(name);
            }
            else
            {
                payload[name] = value;
            }
        }

        var expiry = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(5));

        // Without exp the handler would add one of its own unless told not to.
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = expires }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = expiry.AddMinutes(-10),
            NotBefore = expiry.AddMinutes(-10),
            Expires = expires ? expiry : null,
            TokenType = type,
            SigningCredentials = new SigningCredentials(key ?? Key, SecurityAlgorithms.HmacSha256),
            Claims = payload,
        });
    }

    /// <summary>
    /// A header a test sends to have its call's context signed as asserted, a service's own word.
    /// </summary>
    public const string Asserted = "Test-Asserted";

    /// <summary>
    /// A header a test sends to have its call go unsigned: no context token at all.
    /// </summary>
    public const string Unsigned = "Test-Unsigned";

    /// <summary>
    /// Signs a service's plain headers into a context token, unless the call carries one itself.
    /// </summary>
    private sealed class ContextSigner(bool domainService) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            if (domainService)
            {
                app.Use((http, following) =>
                {
                    var headers = http.Request.Headers;

                    if (!headers.ContainsKey(BitwardenHeaders.Delegation)
                        && !headers.ContainsKey(Unsigned)
                        && headers.Authorization.FirstOrDefault() is { } bearer
                        && bearer.StartsWith("Bearer ", StringComparison.Ordinal)
                        && Client(bearer["Bearer ".Length..]) is { } client
                        && client.StartsWith(InternalCaller.ClientPrefix, StringComparison.Ordinal))
                    {
                        headers[BitwardenHeaders.Delegation] = SignedContext(
                            client,
                            Owner(headers, BitwardenHeaders.Organization),
                            Owner(headers, BitwardenHeaders.User),
                            Owner(headers, BitwardenHeaders.Provider),
                            headers[BitwardenHeaders.Context].Count == 1 ? headers[BitwardenHeaders.Context][0]! : """{"v":1}""",
                            mode: headers.ContainsKey(Asserted) ? CallerContextToken.Asserted : CallerContextToken.Delegated);
                    }

                    return following(http);
                });
            }

            next(app);
        };

        private static string? Client(string token)
        {
            try
            {
                return new JsonWebToken(token).TryGetPayloadValue<string>("client_id", out var client) ? client : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static Guid? Owner(Microsoft.AspNetCore.Http.IHeaderDictionary headers, string name) =>
            headers[name].Count == 1 && Guid.TryParse(headers[name][0], out var owner) && owner != Guid.Empty ? owner : null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync(CancellationToken.None);
        await _app.DisposeAsync();
    }

    [ProductService]
    private class ProductProbeService(
        IReadOnlyDictionary<string, string?> configuration,
        Action<IServiceCollection>? services = null)
        : ProbeService(configuration, services);

    // Deliberately unmarked: it is a product service only through its base.
    private sealed class DerivedProductProbeService(IReadOnlyDictionary<string, string?> configuration)
        : ProductProbeService(configuration);

    private sealed class DomainProbeService(
        IReadOnlyDictionary<string, string?> configuration,
        Action<IServiceCollection>? services)
        : ProbeService(configuration, services);

    private abstract class ProbeService(
        IReadOnlyDictionary<string, string?> configuration,
        Action<IServiceCollection>? extra)
        : Service(ServiceName, "Session Test Service", [])
    {
        protected override AuthorizationDefaults Authorization => new()
        {
            Organization = ContextRequirement.Optional,
            ReadScope = Scope,
            WriteScope = WriteScope,
        };

        protected override WebApplicationBuilder CreateBuilder()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing",
                ApplicationName = typeof(ProbeService).Assembly.GetName().Name,
            });

            builder.WebHost.UseTestServer();

            // An address that resolves nowhere, so a call escaping the answering handler fails loudly.
            builder.Configuration[$"Services:{Downstream}:BaseAddress"] = "http://downstream.invalid";

            foreach (var (key, value) in configuration)
            {
                builder.Configuration[key] = value;
            }

            return builder;
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            // Every outbound connection lands in the answering handler.
            services.ConfigureHttpClientDefaults(client =>
                client.ConfigurePrimaryHttpMessageHandler(() => new DownstreamHandler()));

            services.AddHttpClient<DownstreamClient>((provider, client) =>
                client.BaseAddress = new Uri(provider.GetRequiredService<IConfiguration>()[$"Services:{Downstream}:BaseAddress"]!));

            // The real handler, pointed at a key this process holds (as the platform's own test
            // factory does), so validation is everything a deployed service applies.
            var discovered = configuration.ContainsKey(BearerAuthority);

            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                if (discovered)
                {
                    // The scheme as configuration bound it (no issuer named, so ValidIssuers is an empty
                    // list), with the discovery document Identity would publish: its issuer and key.
                    options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                    options.Configuration.SigningKeys.Add(Key);
                    return;
                }

                options.Authority = null;
                options.MetadataAddress = null!;
                options.RequireHttpsMetadata = false;
                options.Configuration = new OpenIdConnectConfiguration();
                options.TokenValidationParameters.ValidIssuer = Issuer;
                options.TokenValidationParameters.IssuerSigningKey = Key;
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                options.TokenValidationParameters.ValidateLifetime = true;
            });

            // A domain service believes a call only as its context token says: the headers a test
            // sends from a service are signed, as Identity's exchange would sign them.
            services.AddSingleton<IStartupFilter>(new ContextSigner(this is not ProductProbeService));

            extra?.Invoke(services);
        }

        protected override void MapEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints)
        {
            Probes.Map(endpoints);
            Parity.Map(endpoints);
            ParityRaw.Map(endpoints);
            InstallProbesEndpoints.Map(endpoints);
            DatedProbesEndpoints.Map(endpoints);
        }
    }
}

/// <summary>
/// What a probe reports about the request that reached it.
/// </summary>
public sealed record Probe : Resource
{
    /// <summary>
    /// The acting user, as the handler saw it.
    /// </summary>
    [ReadOnly]
    public Guid? ActingUser { get; init; }

    /// <summary>
    /// An ordinary string, held to the platform's default length.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>
    /// Ciphertext, with a bound of its own.
    /// </summary>
    [Encrypted(MaxLength = 10_000)]
    public string? Secret { get; init; }

    /// <summary>
    /// Costs something to work out, so a caller who doesn't need it can leave it out.
    /// </summary>
    [ReadOnly]
    public string? Costly { get; init; }
}

/// <summary>
/// An action's input naming the organization it is about.
/// </summary>
public sealed record NamedOrganization : INamesOrganization
{
    /// <inheritdoc />
    public required Guid OrganizationId { get; init; }
}

/// <summary>
/// An action's input naming the provider it is about.
/// </summary>
public sealed record NamedProvider : INamesProvider
{
    /// <inheritdoc />
    public required Guid ProviderId { get; init; }
}

/// <summary>
/// A client for a domain service, registered as a typed client.
/// </summary>
/// <param name="http">Addressed to the downstream service the host's configuration names.</param>
public sealed class DownstreamClient(HttpClient http)
{
    /// <summary>
    /// Makes one call.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task CallAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync("api/v1/things", cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// Answers every outbound connection the service makes, so no call leaves the process.
/// </summary>
internal sealed class DownstreamHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
}
