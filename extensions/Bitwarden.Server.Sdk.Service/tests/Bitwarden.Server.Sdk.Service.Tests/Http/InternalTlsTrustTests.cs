using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

/// <summary>
/// TLS between a deployment's services: a service served over HTTPS with Kestrel's own settings is
/// called over TLS, trusted through an internal certificate authority named in <c>Tls:CertificateAuthorities</c>, and
/// <c>Tls:RequireHttps</c> refuses plain addresses.
/// </summary>
public sealed class InternalTlsTrustTests : IAsyncLifetime
{
    private readonly string _directory = Directory.CreateTempSubdirectory("internal-tls-").FullName;

    private X509Certificate2 _authority = null!;

    private WebApplication _server = null!;

    private string _address = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        // An internal authority of the test's own, and a server certificate from it for 127.0.0.1, made up per run.
        using var authorityKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var authorityRequest = new CertificateRequest("CN=Internal TLS test authority", authorityKey, HashAlgorithmName.SHA256);
        authorityRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        authorityRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        _authority = authorityRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

        using var serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var serverRequest = new CertificateRequest("CN=127.0.0.1", serverKey, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        serverRequest.CertificateExtensions.Add(names.Build());
        serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var issued = serverRequest.Create(_authority, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(50), [1, 2, 3, 4]);
        var server = X509CertificateLoader.LoadPkcs12(issued.CopyWithPrivateKey(serverKey).Export(X509ContentType.Pkcs12), null);

        File.WriteAllText(Path.Combine(_directory, "authority.pem"), _authority.ExportCertificatePem());

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(server)));
        _server = builder.Build();
        _server.MapGet("/alive", () => "alive");
        // An issuer's discovery document and keys, as Identity publishes them, over the same TLS.
        _server.MapGet("/.well-known/openid-configuration", (HttpContext http) => Results.Json(new Dictionary<string, string>
        {
            ["issuer"] = $"https://{http.Request.Host}",
            ["jwks_uri"] = $"https://{http.Request.Host}/.well-known/jwks",
        }));
        _server.MapGet("/.well-known/jwks", () => Results.Json(new { keys = Array.Empty<object>() }));
        await _server.StartAsync(Token);
        _address = _server.Urls.Single();
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        _authority.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task Call_ToAServiceWhoseAuthorityIsTrusted_Succeeds()
    {
        using var provider = Clients(new InternalTls { CertificateAuthorities = [Path.Combine(_directory, "authority.pem")] });

        var answer = await provider.GetRequiredService<IHttpClientFactory>().CreateClient("callee").GetStringAsync($"{_address}/alive", Token);

        Assert.StartsWith("https://", _address, StringComparison.Ordinal);
        Assert.Equal("alive", answer);
    }

    // The bearer handler fetches Identity's discovery document and keys through a backchannel of its own, built by the
    // framework's post-configure: the trust is set before it, so an internally certified Identity's keys load.
    [Fact]
    public async Task BearerBackchannel_ToAnIssuerWhoseAuthorityIsTrusted_LoadsItsConfiguration()
    {
        using var provider = Bearer(new InternalTls { CertificateAuthorities = [Path.Combine(_directory, "authority.pem")] });
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        var configuration = await options.ConfigurationManager!.GetConfigurationAsync(Token);

        Assert.Equal(_address, configuration.Issuer);
    }

    [Fact]
    public async Task BearerBackchannel_ToAnIssuerWhoseAuthorityIsntTrusted_Fails()
    {
        using var provider = Bearer(new InternalTls());
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        await Assert.ThrowsAnyAsync<Exception>(() => options.ConfigurationManager!.GetConfigurationAsync(Token));
    }

    // Without the authority named, the system's roots don't know it, and the call is refused.
    [Fact]
    public async Task Call_ToAServiceWhoseAuthorityIsntTrusted_Fails()
    {
        using var provider = Clients(new InternalTls());

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.GetRequiredService<IHttpClientFactory>().CreateClient("callee").GetStringAsync($"{_address}/alive", Token));
    }

    // A name that doesn't match is never rescued by the internal authority.
    [Fact]
    public void Trusts_ANameMismatch_Refused()
    {
        var authorities = new X509Certificate2Collection(_authority);

        Assert.False(InternalTls.Trusts(authorities, _authority, null, SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.True(InternalTls.Trusts(authorities, _authority, null, SslPolicyErrors.None));
    }

    [Theory]
    [InlineData("missing.pem")]
    [InlineData("empty.pem")]
    public void Start_AnAuthorityThatCantBeRead_Throws(string file)
    {
        File.WriteAllText(Path.Combine(_directory, "empty.pem"), "not a certificate");

        var exception = Assert.Throws<InvalidOperationException>(
            () => new InternalTls { CertificateAuthorities = [Path.Combine(_directory, file)] }.LoadAuthorities());

        Assert.Contains(file, exception.Message, StringComparison.Ordinal);
    }

    // Tls:RequireHttps: Identity's keys are fetched from an https authority or the service doesn't start.
    [Theory]
    [InlineData("http://identity.internal", false)]
    [InlineData("https://identity.internal", true)]
    [InlineData(null, true)]
    public void RequireHttps_TheBearerAuthority_MustBeHttps(string? authority, bool valid)
    {
        var services = new ServiceCollection();
        InternalTlsTrust.Add(services, new InternalTls { RequireHttps = true });
        using var provider = services.BuildServiceProvider();
        var validator = provider.GetServices<IValidateOptions<JwtBearerOptions>>().Single();

        var result = validator.Validate(JwtBearerDefaults.AuthenticationScheme, new JwtBearerOptions { Authority = authority });

        Assert.Equal(valid, result.Succeeded);
    }

    /// <summary>
    /// The bearer handler as the platform registers it (authentication first, then the trust), pointed at the server.
    /// </summary>
    private ServiceProvider Bearer(InternalTls tls)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication().AddJwtBearer();
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.Authority = _address);
        InternalTlsTrust.Add(services, tls);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider Clients(InternalTls tls)
    {
        var services = new ServiceCollection();
        InternalTlsTrust.Add(services, tls);
        services.AddHttpClient("callee");
        return services.BuildServiceProvider();
    }
}
