using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Bitwarden.Server.Sdk.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

/// <summary>
/// What a service serves under <c>Tls:*</c>: its server certificate from settings, no plain-HTTP listener with
/// <c>Tls:RequireHttps</c> but the health port, and only the probes on that port.
/// </summary>
public sealed class InternalTlsServerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("internal-tls-server-").FullName;

    private readonly X509Certificate2 _authority;

    private readonly ECDsa _authorityKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public InternalTlsServerTests()
    {
        var request = new CertificateRequest("CN=Internal TLS server test authority", _authorityKey, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        _authority = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        File.WriteAllText(AuthorityPath, _authority.ExportCertificatePem());
    }

    private string AuthorityPath => Path.Combine(_directory, "authority.pem");

    public void Dispose()
    {
        _authority.Dispose();
        _authorityKey.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData("http://+:5240", true)]
    [InlineData("http://*:80", true)]
    [InlineData("http://localhost", true)]
    [InlineData("HTTP://[::]:5240", true)]
    [InlineData("http://127.0.0.1:5240/", true)]
    [InlineData("https://+:5240", false)]
    [InlineData("https://[::1]:443", false)]
    [InlineData("http://127.0.0.1:8081", false)]
    [InlineData("", false)]
    public void PlainAddresses_AreEveryHttpAddressButTheHealthPorts(string address, bool plain) =>
        Assert.Equal(plain, InternalTlsServer.PlainAddresses([address], healthPort: 8081).Any());

    // An http address without a port is on port 80: the health port when that is 80, a plain listener otherwise.
    [Theory]
    [InlineData(80, false)]
    [InlineData(81, true)]
    public void PlainAddresses_AnHttpAddressWithoutAPort_IsOnPort80(int healthPort, bool plain) =>
        Assert.Equal(plain, InternalTlsServer.PlainAddresses(["http://localhost"], healthPort).Any());

    // Kestrel's precedence: its endpoints, else urls, else the ports. The .NET images set ASPNETCORE_HTTP_PORTS=8080,
    // which ASPNETCORE_URLS overrides, so it is never taken for a plain listener beside urls.
    [Theory]
    [InlineData(null, null, "8080", null, "http://*:8080")]
    [InlineData(null, null, "8080", "8443", "http://*:8080 https://*:8443")]
    [InlineData(null, "https://+:5240;http://127.0.0.1:8081", "8080", null, "https://+:5240 http://127.0.0.1:8081")]
    [InlineData("http://0.0.0.0:9000", "https://+:5240", "8080", null, "http://0.0.0.0:9000")]
    [InlineData(null, null, null, null, "")]
    public void ConfiguredAddresses_FollowKestrelsPrecedence(string? endpoint, string? urls, string? httpPorts, string? httpsPorts, string expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kestrel:Endpoints:Internal:Url"] = endpoint,
            ["urls"] = urls,
            ["http_ports"] = httpPorts,
            ["https_ports"] = httpsPorts,
        }).Build();

        Assert.Equal(expected, string.Join(" ", InternalTlsServer.ConfiguredAddresses(configuration)));
    }

    // Refused before it listens: the configured address is plain.
    [Fact]
    public async Task Start_WithRequireHttps_AndAPlainConfiguredAddress_IsRefused()
    {
        await using var app = Build(new InternalTls { RequireHttps = true }, builder => builder.WebHost.UseUrls("http://127.0.0.1:0"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync(Token));

        Assert.Contains("Tls:RequireHttps", exception.Message, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:0", exception.Message, StringComparison.Ordinal);
    }

    // Refused once it listens: an address set in code, which no setting names.
    [Fact]
    public async Task Start_WithRequireHttps_AndAPlainListenerSetInCode_IsRefused()
    {
        await using var app = Build(new InternalTls { RequireHttps = true },
            builder => builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync(Token));

        Assert.Contains("listens on plain HTTP", exception.Message, StringComparison.Ordinal);
    }

    // Nothing set, nothing changes: a developer's machine serves plain HTTP as before.
    [Fact]
    public void Add_WithNothingSet_RegistersNothing()
    {
        var services = new ServiceCollection();
        InternalTlsServer.Add(services, new InternalTls());

        Assert.Empty(services);
    }

    // Kestrel's own default, http://localhost:5000, when nothing names an address, is plain, and refused once it listens.
    [Fact]
    public async Task Start_WithRequireHttps_AndNoAddressAtAll_IsRefused()
    {
        await using var app = Build(new InternalTls { RequireHttps = true }, _ => { });

        await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync(Token));
    }

    // The certificate from settings is served, and a caller trusting the internal authority reaches the service.
    [Fact]
    public async Task Start_WithRequireHttps_AndTheCertificateFromSettings_ServesHttps()
    {
        var tls = Server(new InternalTls { RequireHttps = true });
        await using var app = Build(tls, builder => builder.WebHost.UseUrls("https://127.0.0.1:0"));
        await app.StartAsync(Token);

        using var client = Caller();
        var answer = await client.GetStringAsync($"{app.Urls.Single()}/alive", Token);

        Assert.StartsWith("https://", app.Urls.Single(), StringComparison.Ordinal);
        Assert.Equal("alive", answer);
    }

    // A plain listener on the health port is allowed, and answers the probes alone.
    [Fact]
    public async Task HealthPort_AllowsAPlainListener_ThatAnswersOnlyTheProbes()
    {
        var healthPort = FreePort();
        var tls = Server(new InternalTls { RequireHttps = true, HealthPort = healthPort });
        await using var app = Build(tls, builder => builder.WebHost.UseUrls($"https://127.0.0.1:0;http://127.0.0.1:{healthPort}"));
        app.MapGet("/api/v1/health", () => "healthy");
        await app.StartAsync(Token);
        var secure = app.Urls.Single(u => u.StartsWith("https://", StringComparison.Ordinal));

        using var plain = new HttpClient();
        var probe = await plain.GetAsync($"http://127.0.0.1:{healthPort}/api/v1/health", Token);
        var other = await plain.GetAsync($"http://127.0.0.1:{healthPort}/alive", Token);
        using var client = Caller();
        var overTls = await client.GetStringAsync($"{secure}/alive", Token);

        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
        Assert.Equal("healthy", await probe.Content.ReadAsStringAsync(Token));
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal("alive", overTls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void HealthPort_ThatIsNoPort_Throws(int port) =>
        Assert.Throws<InvalidOperationException>(() => InternalTlsServer.Add(new ServiceCollection(), new InternalTls { HealthPort = port }));

    // A certificate that can't be read stops the service as it is set up, naming the setting and never the value.
    [Fact]
    public void Add_ACertificateThatCantBeRead_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            InternalTlsServer.Add(new ServiceCollection(), new InternalTls { ServerCertificate = "bm90IGEgY2VydA==", ServerKey = "c2VjcmV0" }));

        Assert.Contains("Tls:ServerCertificate", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("c2VjcmV0", exception.Message, StringComparison.Ordinal);
    }

    private static WebApplication Build(InternalTls tls, Action<WebApplicationBuilder> configure)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrelHttpsConfiguration();
        configure(builder);
        InternalTlsServer.Add(builder.Services, tls);
        var app = builder.Build();
        app.MapGet("/alive", () => "alive");
        return app;
    }

    /// <summary>
    /// A server certificate for 127.0.0.1 from the test's authority, in <paramref name="tls"/>'s settings.
    /// </summary>
    private InternalTls Server(InternalTls tls)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var issued = request.Create(_authority, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(50), [5, 6, 7, 8]);
        tls.ServerCertificate = Convert.ToBase64String(issued.RawData);
        tls.ServerKey = Convert.ToBase64String(key.ExportPkcs8PrivateKey());
        return tls;
    }

    /// <summary>
    /// A client trusting the test's authority, as a service with it in <c>Tls:CertificateAuthorities</c>.
    /// </summary>
    private HttpClient Caller()
    {
        var services = new ServiceCollection();
        InternalTlsTrust.Add(services, new InternalTls { CertificateAuthorities = [AuthorityPath] });
        services.AddHttpClient("callee");
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IHttpClientFactory>().CreateClient("callee");
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
