using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Bitwarden.Server.Sdk.Http.Tests;

/// <summary>
/// What the guard relies on <see cref="SocketsHttpHandler"/> to do with a request it addressed to the checked IP, so a
/// change in the runtime fails here rather than in production.
/// </summary>
public class SocketsHttpHandlerTests
{
    // The guard addresses an https request to the checked IP, with the host in the Host header: TLS must still ask for
    // the host by name and check the certificate against it, and the connection must go to the IP.
    [Fact]
    public async Task SendAsync_AnHttpsRequestAddressedToAnIp_UsesTheHostHeaderForTls()
    {
        using var certificate = SelfSigned("service.test");
        using var server = new TlsServer(TlsServer.Serving(certificate));
        var connectedTo = new List<string>();
        // Trusts the test's certificate alone, with no callback: a name that doesn't match fails the request.
        using var invoker = new HttpMessageInvoker(server.Client(Trusting(certificate), connectedTo));
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://93.184.216.34:{server.Port}/");
        request.Headers.Host = $"service.test:{server.Port}";

        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["93.184.216.34"], connectedTo);
        Assert.Equal("service.test", server.ServerName);
    }

    // The same IP named by two hosts gets a connection, and a TLS session, for each.
    [Fact]
    public async Task SendAsync_TwoHostsAtOneIp_DoNotShareAConnection()
    {
        using var first = SelfSigned("first.test");
        using var second = SelfSigned("second.test");
        using var server = new TlsServer(TlsServer.Serving(first), TlsServer.Serving(second));
        var connectedTo = new List<string>();
        using var invoker = new HttpMessageInvoker(server.Client(Trusting(first, second), connectedTo));

        foreach (var host in (string[])["first.test", "second.test"])
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://93.184.216.34:{server.Port}/");
            request.Headers.Host = $"{host}:{server.Port}";
            using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(2, connectedTo.Count);
    }

    private static X509ChainPolicy Trusting(params X509Certificate2[] roots)
    {
        var policy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
        };
        policy.CustomTrustStore.AddRange(roots);
        return policy;
    }

    private static X509Certificate2 SelfSigned(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(name);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        // Exported and read back, so SslStream can use the key on every operating system.
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12), null);
    }
}
