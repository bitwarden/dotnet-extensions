using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Bitwarden.Server.Sdk.Authentication.Tests;

public class AuthenticationServiceCollectionExtensionsTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public AuthenticationServiceCollectionExtensionsTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task AddBitwardenAuthentication_NoBearerToken_Unauthorized()
    {
        using var authHost = CreateAuthHost(out _);
        await authHost.StartAsync(TestContext.Current.CancellationToken);
        using var appHost = CreateAppHost(authHost);
        await appHost.StartAsync(TestContext.Current.CancellationToken);

        var client = appHost.GetTestClient();

        var response = await client.GetAsync("/authed-user", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AddBitwardenAuthentication_QueryString_DoesNotWork()
    {
        using var authHost = CreateAuthHost(out var certificate);
        await authHost.StartAsync(TestContext.Current.CancellationToken);
        using var appHost = CreateAppHost(authHost);

        await appHost.StartAsync(TestContext.Current.CancellationToken);

        var client = appHost.GetTestClient();

        var accessToken = CreateAccessToken(certificate,
            new JwtPayload(issuer: "http://localhost", audience: null, claims: null, notBefore: DateTime.UtcNow, expires: DateTime.UtcNow.AddDays(1)));

        var response = await client.GetAsync($"/authed-user?access_token={accessToken}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AddBitwardenAuthentication_Header_Works()
    {
        using var authHost = CreateAuthHost(out var certificate);
        await authHost.StartAsync(TestContext.Current.CancellationToken);
        using var appHost = CreateAppHost(authHost);

        await appHost.StartAsync(TestContext.Current.CancellationToken);

        var client = appHost.GetTestClient();

        var accessToken = CreateAccessToken(certificate,
            new JwtPayload(issuer: "http://localhost", audience: null, claims: null, notBefore: DateTime.UtcNow, expires: DateTime.UtcNow.AddDays(1)));

        var request = new HttpRequestMessage(HttpMethod.Get, "/authed-user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AddBitwardenAuthentication_DoesNotMapClaims()
    {
        using var authHost = CreateAuthHost(out var certificate);
        await authHost.StartAsync(TestContext.Current.CancellationToken);

        using var appHost = CreateAppHost(authHost, configureServices: null,
            endpoints =>
            {
                endpoints.MapGet("/test", (ClaimsPrincipal user) =>
                {
                    // Claim should NOT exist with its mapped name
                    Assert.DoesNotContain(user.Claims, c => c.Type == ClaimTypes.AuthenticationMethod);
                    // Claim SHOULD exist with it's original name
                    var claim = Assert.Single(user.Claims, c => c.Type == JwtRegisteredClaimNames.Amr);
                    return Results.Text(claim.Value);
                });
            }
        );

        await appHost.StartAsync(TestContext.Current.CancellationToken);

        var client = appHost.GetTestClient();

        var accessToken = CreateAccessToken(certificate,
            new JwtPayload(issuer: "http://localhost", audience: null, claims: [
                new Claim(JwtRegisteredClaimNames.Amr, "test")
            ], notBefore: DateTime.UtcNow, expires: DateTime.UtcNow.AddDays(1)));

        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("test", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddBitwardenAuthentication_UsesEmailForNameClaimType()
    {
        using var authHost = CreateAuthHost(out var certificate);
        await authHost.StartAsync(TestContext.Current.CancellationToken);

        using var appHost = CreateAppHost(authHost, configureServices: null,
            endpoints =>
            {
                endpoints.MapGet("/test", (ClaimsPrincipal user) =>
                {
                    Assert.NotNull(user.Identity);
                    return Results.Text(user.Identity.Name);
                });
            }
        );

        await appHost.StartAsync(TestContext.Current.CancellationToken);

        var client = appHost.GetTestClient();

        var accessToken = CreateAccessToken(certificate,
            new JwtPayload(issuer: "http://localhost", audience: null, claims: [
                new Claim(JwtRegisteredClaimNames.Email, "test@example.com")
            ], notBefore: DateTime.UtcNow, expires: DateTime.UtcNow.AddDays(1)));

        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("test@example.com", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddBitwardenAuthentication_LogInEndpoint_ContainsScope()
    {
        using var authHost = CreateAuthHost(out var certificate);
        await authHost.StartAsync(TestContext.Current.CancellationToken);

        using var appHost = CreateAppHost(authHost, configureServices: null,
            endpoints =>
            {
                endpoints.MapGet("/test", (ILoggerFactory loggerFactory) =>
                {
                    loggerFactory.CreateLogger("Test").LogWarning("Hello world!");
                });
            }
        );

        await appHost.StartAsync(TestContext.Current.CancellationToken);

        var client = appHost.GetTestClient();

        var accessToken = CreateAccessToken(certificate,
            new JwtPayload(issuer: "http://localhost", audience: null, claims: [
                new Claim(JwtRegisteredClaimNames.Sub, "user-subject")
            ], notBefore: DateTime.UtcNow, expires: DateTime.UtcNow.AddDays(1)));

        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var logCollector = appHost.Services.GetFakeLogCollector();
        var logs = logCollector.GetSnapshot(true);
        var myLog = Assert.Single(logs, l => l.Category == "Test" && l.Level == LogLevel.Warning && l.Message == "Hello world!");
        Assert.Contains(myLog.Scopes.OfType<IEnumerable<KeyValuePair<string, object>>>(),
            scope => scope.Any(kvp => kvp.Key == "Subject" && kvp.Value is string stringVal && stringVal == "user-subject")
        );
    }

    private IHost CreateAppHost(
        IHost authHost,
        Action<IServiceCollection>? configureServices = null,
        Action<IEndpointRouteBuilder>? configureEndpoints = null
    )
    {
        return CreateHost(
            (services) =>
            {
                services.AddBitwardenAuthentication();

                if (authHost != null)
                {
                    // Shove in the auth host handler so that communication can be made to it.
                    services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    {
                        options.BackchannelHttpHandler = authHost.GetTestServer().CreateHandler();
                    });
                }

                services.AddAuthorizationBuilder()
                    .AddPolicy("authed-user", (policy) => policy.RequireAuthenticatedUser())
                    .AddPolicy("simple-user", (policy) => policy.RequireAuthenticatedUser().RequireClaim("simple")
                );

                configureServices?.Invoke(services);
            },
            (app) =>
            {
                app.UseBitwardenAuthentication();
                app.UseAuthorization();

                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/anonymous", () =>
                    {
                        return Results.Ok(new { Message = "Hi!" });
                    })
                        .AllowAnonymous();

                    endpoints.MapGet("authed-user", () =>
                    {
                        return Results.Ok(new { Message = "You are authed!" });
                    })
                        .RequireAuthorization("authed-user");

                    endpoints.MapGet("/simple-user", (ClaimsPrincipal user) =>
                    {
                        return Results.Ok(new { Message = $"Claim value: {user.FindFirstValue("simple")}" });
                    })
                        .RequireAuthorization("simple-user");

                    configureEndpoints?.Invoke(endpoints);
                });
            },
            new Dictionary<string, string?>
            {
                { "Authentication:Schemes:Bearer:Authority", "http://localhost" },
                { "Authentication:Schemes:Bearer:RequireHttpsMetadata", "false" },
            },
            "App"
        );
    }

    private static string CreateAccessToken(X509Certificate2 certificate, JwtPayload payload)
    {
        var handler = new JwtSecurityTokenHandler();
        var token = new JwtSecurityToken(
            new JwtHeader(new SigningCredentials(new X509SecurityKey(certificate), SecurityAlgorithms.RsaSha256), null, "at+jwt"),
            payload
        );
        return handler.WriteToken(token);
    }

    private IHost CreateAuthHost(out X509Certificate2 certificate)
    {
        using var rsa = RSA.Create(2048);
        var certRequest = new CertificateRequest($"CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var selfSignedCert = certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddYears(-1).AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(-1));
        certificate = selfSignedCert;

        return CreateHost(
            configureServices: null,
            configureApplication: (app) =>
            {
                app.UseEndpoints(endpoints =>
                {
                    // Stub out the minimal things needed from an OpenId compatible server
                    endpoints.MapGet("/.well-known/openid-configuration", () =>
                    {
                        return Results.Json(new OpenIdConnectConfiguration
                        {
                            Issuer = "http://localhost",
                            JwksUri = "http://localhost/.well-known/openid-configuration/jwks",
                        });
                    });

                    endpoints.MapGet("/.well-known/openid-configuration/jwks", () =>
                    {
                        var parameters = RSACertificateExtensions.GetRSAPublicKey(selfSignedCert)!.ExportParameters(false);
                        var keySet = new JsonWebKeySet();
                        keySet.Keys.Add(new JsonWebKey
                        {
                            Kty = JsonWebAlgorithmsKeyTypes.RSA,
                            Use = "sig",
                            Kid = selfSignedCert.Thumbprint + JsonWebAlgorithmsKeyTypes.RSA,
                            X5t = Base64UrlEncoder.Encode(selfSignedCert.GetCertHash()),
                            E = Base64UrlEncoder.Encode(parameters.Exponent),
                            N = Base64UrlEncoder.Encode(parameters.Modulus),
                            X5c = { Convert.ToBase64String(selfSignedCert.RawDataMemory.Span) },
                            Alg = SecurityAlgorithms.RsaSha256,
                        });
                        return Results.Json(keySet);
                    });
                });
            },
            configuration: null,
            "Auth"
        );
    }

    private IHost CreateHost(
        Action<IServiceCollection>? configureServices,
        Action<IApplicationBuilder> configureApplication,
        Dictionary<string, string?>? configuration,
        string name
    )
    {
        return new HostBuilder()
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(builder =>
            {
                builder.AddInMemoryCollection(configuration);
            })
            .ConfigureWebHost((webHostBuilder) =>
            {
                webHostBuilder
                    .UseTestServer()
                    .ConfigureLogging(logging =>
                    {
                        logging.SetMinimumLevel(LogLevel.Trace);
                        logging.AddProvider(new XUnitLoggerProvider(name, _testOutputHelper));
                        logging.AddFakeLogging();
                    })
                    .ConfigureServices((context, services) =>
                    {
                        services.AddRouting();

                        configureServices?.Invoke(services);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();

                        configureApplication.Invoke(app);
                    });
            })
            .Build();
    }

    public sealed class XUnitLoggerProvider : ILoggerProvider
    {
        private readonly string _name;
        private readonly ITestOutputHelper _testOutputHelper;

        public XUnitLoggerProvider(string name, ITestOutputHelper testOutputHelper)
        {
            _name = name;
            _testOutputHelper = testOutputHelper;
        }
        public ILogger CreateLogger(string categoryName) => new XUnitLogger(_name, categoryName, _testOutputHelper);
        public void Dispose() { }

        private sealed class XUnitLogger : ILogger
        {
            private readonly string _name;
            private readonly string _categoryName;
            private readonly ITestOutputHelper _testOutputHelper;

            public XUnitLogger(string name, string categoryName, ITestOutputHelper testOutputHelper)
            {
                _name = name;
                _categoryName = categoryName;
                _testOutputHelper = testOutputHelper;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _testOutputHelper.WriteLine($"[{_name}][{_categoryName}] {formatter(state, exception)}");
            }
        }
    }
}
