using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// TrustInternalAuthorities: a client calling the deployment's own services trusts the internal authorities named in
/// Tls:CertificateAuthorities and nothing else; with none named it is left as it was. Only the clients that call our own
/// services get it.
/// </summary>
public sealed class InternalAuthorityTrustTests : IDisposable
{
    private const string Host = "widget-service";
    private const string Callee = "callee-service";

    private readonly string _directory = Directory.CreateTempSubdirectory().FullName;

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task SendAsync_AChainToANamedAuthority_IsTrusted()
    {
        using var authority = Authority("Internal");
        using var served = Issue(authority);
        using var server = new TlsServer(TlsServer.Serving(served));

        var status = await CallAsync(server, Authorities(authority));

        Assert.Equal(HttpStatusCode.OK, status);
    }

    // The system's roots aren't asked: a chain to any other root fails, as one to a public authority would.
    [Fact]
    public async Task SendAsync_AChainToAnotherAuthority_IsRefused()
    {
        using var named = Authority("Internal");
        using var other = Authority("Other");
        using var served = Issue(other);
        using var server = new TlsServer(TlsServer.Serving(served));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => CallAsync(server, Authorities(named)));

        Assert.IsType<AuthenticationException>(exception.InnerException);
    }

    [Fact]
    public void ANamedAuthority_IsTheOnlyRootTrusted()
    {
        using var authority = Authority("Internal");
        using var provider = Services(Authorities(authority), services => services.AddHttpClient("internal").TrustInternalAuthorities());

        var policy = PolicyOf(provider, "internal");

        Assert.NotNull(policy);
        Assert.Equal(X509ChainTrustMode.CustomRootTrust, policy.TrustMode);
        Assert.Equal(authority.Thumbprint, Assert.Single(policy.CustomTrustStore).Thumbprint);
    }

    [Fact]
    public void NoAuthorityNamed_LeavesTheSystemsRoots()
    {
        using var provider = Services([], services => services.AddHttpClient("internal").TrustInternalAuthorities());

        Assert.Null(PolicyOf(provider, "internal"));
    }

    // A client that sets its own chain policy first keeps it.
    [Fact]
    public void AHandlerWithItsOwnPolicy_KeepsIt()
    {
        using var authority = Authority("Internal");
        var own = new X509ChainPolicy();
        using var provider = Services(Authorities(authority), services => services.AddHttpClient("internal")
            .UseSocketsHttpHandler((handler, _) => handler.SslOptions.CertificateChainPolicy = own)
            .TrustInternalAuthorities());

        Assert.Same(own, PolicyOf(provider, "internal"));
    }

    [Fact]
    public void AnAuthorityThatCannotBeRead_StopsTheHostAtStart()
    {
        using var provider = Services([Path.Combine(_directory, "missing.pem")], services => services.AddHttpClient("internal").TrustInternalAuthorities());

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("Tls:CertificateAuthorities", exception.Message, StringComparison.Ordinal);
    }

    // The clients that call our own services get the authorities; any other client the host registers is left alone.
    [Fact]
    public void TheServiceTheTokenAndTheProductClients_TrustTheAuthorities_AndNoOther()
    {
        using var authority = Authority("Internal");
        string? service = null;
        string? product = null;
        using var provider = Services(Authorities(authority), services =>
        {
            service = services.AddBitwardenClient<CalleeClient, CalleeClient>(Callee, ["callee.read"]).Name;
            product = services.AddProductClient<ProductClientTests.TestClient, ProductClientTests.TestClient>("product-service").Name;
            services.AddHttpClient("unrelated");
        });

        Assert.Equal(X509ChainTrustMode.CustomRootTrust, PolicyOf(provider, service!)?.TrustMode);
        Assert.Equal(X509ChainTrustMode.CustomRootTrust, PolicyOf(provider, ServiceTokenProvider.TokenClientName)?.TrustMode);
        Assert.Equal(X509ChainTrustMode.CustomRootTrust, PolicyOf(provider, product!)?.TrustMode);
        Assert.Null(PolicyOf(provider, "unrelated"));
    }

    private async Task<HttpStatusCode> CallAsync(TlsServer server, string[] authorities)
    {
        using var provider = Services(authorities, services => services.AddHttpClient("internal")
            .TrustInternalAuthorities()
            .UseSocketsHttpHandler((handler, _) => handler.ConnectCallback = server.ConnectAsync));
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("internal");

        using var response = await client.GetAsync(new Uri($"https://{Host}:{server.Port}/"), TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private static ServiceProvider Services(string[] authorities, Action<IServiceCollection> register)
    {
        var settings = new Dictionary<string, string?>
        {
            [$"Services:{Callee}:BaseAddress"] = "https://callee.internal",
            [$"Services:{Callee}:IdentityUri"] = "https://identity.internal",
            [$"Services:{Callee}:CallerName"] = "caller",
            [$"Services:{Callee}:CallerSecret"] = "made-up",
        };
        for (var index = 0; index < authorities.Length; index++)
        {
            settings[$"Tls:CertificateAuthorities:{index}"] = authorities[index];
        }

        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .AddLogging();
        register(services);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The chain policy of the handler that sends a named client's requests, or null when it has none.
    /// </summary>
    private static X509ChainPolicy? PolicyOf(IServiceProvider provider, string name)
    {
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(name);
        while (handler is DelegatingHandler { InnerHandler: { } inner })
        {
            handler = inner;
        }

        return (handler as SocketsHttpHandler)?.SslOptions.CertificateChainPolicy;
    }

    private string[] Authorities(X509Certificate2 authority)
    {
        var path = Path.Combine(_directory, $"{authority.Thumbprint}.pem");
        File.WriteAllText(path, authority.ExportCertificatePem());
        return [path];
    }

    private static X509Certificate2 Authority(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return Reloaded(created);
    }

    /// <summary>
    /// A server certificate for <see cref="Host"/>, with its key.
    /// </summary>
    private static X509Certificate2 Issue(X509Certificate2 authority)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={Host}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(Host);
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.Create(authority, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(12), [1, 2, 3, 4]);
        using var withKey = certificate.CopyWithPrivateKey(key);
        return Reloaded(withKey);
    }

    /// <summary>
    /// Exported and read back, so SslStream can use the key on every operating system.
    /// </summary>
    private static X509Certificate2 Reloaded(X509Certificate2 certificate) =>
        X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), null);

    public sealed class CalleeClient(HttpClient http)
    {
        public HttpClient Http { get; } = http;
    }
}
