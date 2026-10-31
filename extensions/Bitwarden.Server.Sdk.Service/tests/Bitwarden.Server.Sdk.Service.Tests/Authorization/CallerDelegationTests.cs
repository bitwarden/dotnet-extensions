using System.Net;
using System.Text;
using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// A domain service believes another service's call only as the context token Identity signed for
/// it says (<see cref="BitwardenHeaders.Delegation"/>).
/// </summary>
public sealed class CallerDelegationTests : IAsyncLifetime
{
    private static readonly Guid Named = new("c4000000-0000-4000-8000-00000000000c");

    private SessionHost _domain = null!;

    public async ValueTask InitializeAsync() => _domain = await SessionHost.StartAsync(productService: false);

    public async ValueTask DisposeAsync() => await _domain.DisposeAsync();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // The context comes from the token alone: no plain header needed.
    [Fact]
    public async Task Probe_TheTokenNamingAUser_ActsAsThatUser()
    {
        var response = await Probe(SessionHost.SignedContext(user: Named));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Named, await ProductServiceAttributeTests.ActingUser(response));
    }

    // As a deployed service: its bearer scheme bound from configuration, the issuer only in the discovery document.
    [Fact]
    public async Task Probe_SchemeBoundFromConfiguration_ActsAsTheTokensUser()
    {
        await using var bound = await SessionHost.StartAsync(
            productService: false,
            new Dictionary<string, string?> { [SessionHost.BearerAuthority] = SessionHost.Issuer });

        var response = await Probe(SessionHost.SignedContext(user: Named), host: bound);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Named, await ProductServiceAttributeTests.ActingUser(response));
    }

    // A call asserting nothing needs nothing signed (a deployment strips every caller-context header at its ingress): its context is empty.
    [Fact]
    public async Task Probe_NoContextTokenAndNoContext_ActsForNobody()
    {
        var response = await Probe(signed: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await ProductServiceAttributeTests.ActingUser(response));
    }

    // Asserting something unsigned is refused.
    [Fact]
    public async Task Probe_NoContextToken_Unauthenticated401()
    {
        var response = await Probe(signed: null, user: Named);

        await AssertRefused(response, HttpStatusCode.Unauthorized, BitwardenHeaders.Delegation);
    }

    // Forged (another key), expired, for another callee, or not a context token at all.
    [Theory]
    [InlineData("forged")]
    [InlineData("expired")]
    [InlineData("another-callee")]
    [InlineData("an-access-token")]
    public async Task Probe_AContextTokenThisServiceDoesntAccept_Unauthenticated401(string kind)
    {
        var signed = kind switch
        {
            "expired" => SessionHost.SignedContext(user: Named, lifetime: TimeSpan.FromMinutes(-10)),
            "another-callee" => SessionHost.SignedContext(user: Named, audience: "another-service"),
            "an-access-token" => SessionHost.SignedContext(user: Named, type: "at+jwt"),
            _ => SessionHost.SignedContext(user: Named)[..^4] + "AAAA",
        };

        var response = await Probe(signed);

        await AssertRefused(response, HttpStatusCode.Unauthorized, BitwardenHeaders.Delegation);
    }

    // Bound to the caller: one taken from another service's call is useless from this one.
    [Fact]
    public async Task Probe_AContextTokenIssuedToAnotherCaller_Unauthenticated401()
    {
        var response = await Probe(SessionHost.SignedContext(client: "internal.another-service", user: Named));

        await AssertRefused(response, HttpStatusCode.Unauthorized, BitwardenHeaders.Delegation);
    }

    // Neither a token from another issuer nor one that never expires.
    [Theory]
    [InlineData("another-issuer")]
    [InlineData("no-expiry")]
    public async Task Probe_AContextTokenFromAnotherIssuerOrWithoutExpiry_Unauthenticated401(string kind)
    {
        var signed = kind == "another-issuer"
            ? SessionHost.SignedContext(user: Named, issuer: "https://identity.elsewhere.test")
            : SessionHost.SignedContext(user: Named, expires: false);

        var response = await Probe(signed);

        await AssertRefused(response, HttpStatusCode.Unauthorized, BitwardenHeaders.Delegation);
    }

    // A token saying neither delegated nor asserted says nothing this service can act on.
    [Fact]
    public async Task Probe_AContextTokenWithAnUnknownMode_Unauthenticated401()
    {
        var response = await Probe(SessionHost.SignedContext(user: Named, mode: "impersonated"));

        await AssertRefused(response, HttpStatusCode.Unauthorized, BitwardenHeaders.Delegation);
    }

    // An owner is an identifier: not any other text, not the empty one, not a number or a list.
    [Theory]
    [InlineData("text")]
    [InlineData("empty")]
    [InlineData("number")]
    [InlineData("array")]
    public async Task Probe_AContextTokenNamingAnOwnerThatIsNotAnIdentifier_Unauthenticated401(string kind)
    {
        object owner = kind switch
        {
            "text" => "not-an-identifier",
            "empty" => Guid.Empty.ToString(),
            "number" => 42,
            _ => new[] { Named.ToString() },
        };

        var response = await Probe(SessionHost.SignedContext(
            claims: new Dictionary<string, object?> { [CallerContextToken.UserClaim] = owner }));

        await AssertRefused(response, HttpStatusCode.Unauthorized, BitwardenHeaders.Delegation);
    }

    // Signed, but its context object is not one: the caller's mistake, answered against the context header.
    [Fact]
    public async Task Probe_AContextTokenWithAMalformedContext_Refused400()
    {
        var response = await Probe(SessionHost.SignedContext(user: Named, context: "not-a-context"));

        await AssertRefused(response, HttpStatusCode.BadRequest, BitwardenHeaders.Context);
    }

    // Each caller-context header says one thing: sent twice, the call is refused, whatever the two say.
    [Theory]
    [InlineData(BitwardenHeaders.Organization)]
    [InlineData(BitwardenHeaders.User)]
    [InlineData(BitwardenHeaders.Provider)]
    [InlineData(BitwardenHeaders.Context)]
    [InlineData(BitwardenHeaders.Delegation)]
    public async Task Probe_ACallerContextHeaderSentTwice_Refused400(string header)
    {
        var signed = SessionHost.SignedContext(organization: Named, user: Named, provider: Named);
        var value = header switch
        {
            BitwardenHeaders.Delegation => signed,
            BitwardenHeaders.Context => """{"v":1}""",
            _ => Named.ToString(),
        };
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/current", SessionHost.Token(user: null, SessionHost.Scope));
        if (header != BitwardenHeaders.Delegation)
        {
            request.Headers.Add(BitwardenHeaders.Delegation, signed);
        }

        request.Headers.TryAddWithoutValidation(header, [value, value]);

        using var response = await _domain.Client().SendAsync(request, Token);

        await AssertRefused(response, HttpStatusCode.BadRequest, header);
    }

    // Identity rotated its key: one this service hasn't seen is looked up once, and the token accepted with it.
    [Fact]
    public async Task Probe_AContextTokenSignedWithARotatedKey_AcceptedAfterOneRefresh()
    {
        var rotated = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("rotated-test-signing-key-signs-nothing-real-either"))
        {
            KeyId = "rotated",
        };
        var keys = new RotatingKeys(rotated);
        await using var host = await SessionHost.StartAsync(
            productService: false,
            services: services => services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Configuration = null;
                options.ConfigurationManager = keys;
            }));

        var response = await Probe(SessionHost.SignedContext(user: Named, key: rotated), host: host);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Named, await ProductServiceAttributeTests.ActingUser(response));
        Assert.Equal(1, keys.Refreshes);
    }

    // The plain headers stay the readable copy, and must say what the token says.
    [Theory]
    [InlineData(BitwardenHeaders.Organization)]
    [InlineData(BitwardenHeaders.User)]
    [InlineData(BitwardenHeaders.Provider)]
    [InlineData(BitwardenHeaders.Context)]
    public async Task Probe_APlainHeaderDisagreeingWithTheToken_Refused400(string header)
    {
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/current", SessionHost.Token(user: null, SessionHost.Scope));
        request.Headers.Add(BitwardenHeaders.Delegation, SessionHost.SignedContext(organization: Named, user: Named, provider: Named));
        request.Headers.TryAddWithoutValidation(header, header == BitwardenHeaders.Context ? """{"v":1,"credential":"SYSTEM"}""" : Guid.NewGuid().ToString());

        using var response = await _domain.Client().SendAsync(request, Token);

        await AssertRefused(response, HttpStatusCode.BadRequest, header);
    }

    // A person's token gets no context, whatever it carries: the context token is believed from a service alone.
    [Fact]
    public async Task Probe_APersonsTokenWithAContextToken_ActsForNobody()
    {
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/current", SessionHost.Token(Guid.NewGuid(), SessionHost.Scope));
        request.Headers.Add(BitwardenHeaders.Delegation, SessionHost.SignedContext(user: Named));

        using var response = await _domain.Client().SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await ProductServiceAttributeTests.ActingUser(response));
    }

    // From outside, a product service refuses it as it refuses the other four.
    [Fact]
    public async Task ProductService_AContextTokenFromOutside_Refused400()
    {
        await using var product = await SessionHost.StartAsync(productService: true);
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/current", SessionHost.Token(Named, SessionHost.Scope));
        request.Headers.Add(BitwardenHeaders.Delegation, SessionHost.SignedContext(user: Named));

        using var response = await product.Client().SendAsync(request, Token);

        await AssertRefused(response, HttpStatusCode.BadRequest, BitwardenHeaders.Delegation);
    }

    // --- Delegated only, unless an operation says who may assert a person there --------------------------------

    // A service's own word naming a person, at an operation acting for people: refused, whoever's credential it is.
    [Fact]
    public async Task Probe_AnAssertedContextNamingAPerson_Forbidden403()
    {
        var response = await Probe(SessionHost.SignedContext(user: Named, mode: CallerContextToken.Asserted));

        await AssertRefused(response, HttpStatusCode.Forbidden, BitwardenHeaders.Delegation);
    }

    // A further hop of an asserted context was admitted where it was first asserted: only a live call into a service
    // gives it a context token to pass on, so one credential alone can't have one signed.
    [Fact]
    public async Task Probe_AFurtherHopOfAnAssertedContextNamingAPerson_Accepted()
    {
        var response = await Probe(SessionHost.SignedContext(user: Named, mode: CallerContextToken.Asserted, hops: 2));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Named, await ProductServiceAttributeTests.ActingUser(response));
    }

    // With no act chain, or one that is not an object or ends in one that is not, the call counts one hop: an asserted person is refused.
    [Theory]
    [InlineData("none")]
    [InlineData("not-an-object")]
    [InlineData("ending-in-one-that-is-not-an-object")]
    public async Task Probe_AnAssertedContextNamingAPersonWithoutAnActorChain_CountsAsTheFirstHop(string kind)
    {
        object? actor = kind switch
        {
            "none" => null,
            "not-an-object" => "internal.first-service",
            _ => new Dictionary<string, object>
            {
                ["sub"] = "internal.first-service",
                [CallerContextToken.ActorClaim] = "internal.earlier-service",
            },
        };

        var response = await Probe(SessionHost.SignedContext(
            user: Named,
            mode: CallerContextToken.Asserted,
            claims: new Dictionary<string, object?> { [CallerContextToken.ActorClaim] = actor }));

        await AssertRefused(response, HttpStatusCode.Forbidden, BitwardenHeaders.Delegation);
    }

    // Naming nobody, it is the service's own call, bounded by its scopes.
    [Fact]
    public async Task Probe_AnAssertedContextNamingNobody_Accepted()
    {
        var response = await Probe(SessionHost.SignedContext(organization: Guid.NewGuid(), mode: CallerContextToken.Asserted));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // At an operation that names the caller as one that may assert a person, accepted; at one naming another, refused.
    [Theory]
    [InlineData("system-about-person", HttpStatusCode.OK)]
    [InlineData("another-system-about-person", HttpStatusCode.Forbidden)]
    public async Task Probe_AnAssertedContextNamingAPerson_OnlyWhereTheOperationAdmitsTheCaller(string probe, HttpStatusCode expected)
    {
        using var request = ProductServiceAttributeTests.Get($"/api/v1/probes/{probe}", SessionHost.Token(user: null, SessionHost.Scope));
        request.Headers.Add(BitwardenHeaders.Delegation, SessionHost.SignedContext(user: Named, mode: CallerContextToken.Asserted));

        using var response = await _domain.Client().SendAsync(request, Token);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal(Named, await ProductServiceAttributeTests.ActingUser(response));
        }
    }

    // Outside a request (a message consumer, a job), nothing proves whom a call is for.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CallerProof_OutsideARequest_IsNone(bool productService)
    {
        var proof = new CallerProof(new Microsoft.AspNetCore.Http.HttpContextAccessor(), new Context(new Microsoft.AspNetCore.Http.HttpContextAccessor(), productService), productService);

        Assert.Null(proof.SubjectToken);
    }

    private async Task<HttpResponseMessage> Probe(string? signed, Guid? user = null, SessionHost? host = null)
    {
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/current", SessionHost.Token(user: null, SessionHost.Scope));
        request.Headers.TryAddWithoutValidation(signed is null ? SessionHost.Unsigned : BitwardenHeaders.Delegation, signed ?? "yes");

        if (user is not null)
        {
            request.Headers.Add(BitwardenHeaders.User, user.Value.ToString());
        }

        return await (host ?? _domain).Client().SendAsync(request, Token);
    }

    private static async Task AssertRefused(HttpResponseMessage response, HttpStatusCode status, string header)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var error = document.RootElement.GetProperty("errors")[0];
        Assert.Equal(header, error.GetProperty("source").GetProperty("header").GetString());
    }

    /// <summary>
    /// Identity's published keys, which hold the rotated key only once a refresh is asked for.
    /// </summary>
    private sealed class RotatingKeys(SecurityKey rotated) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public int Refreshes { get; private set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            var configuration = new OpenIdConnectConfiguration();
            if (Refreshes > 0)
            {
                configuration.SigningKeys.Add(rotated);
            }

            return Task.FromResult(configuration);
        }

        public void RequestRefresh() => Refreshes++;
    }
}
