using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

public sealed class ServiceTlsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("service-tls-").FullName;

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Defaults_ServeNothingOfTheirOwnAndAllowHttp()
    {
        var tls = new ServiceTls();

        Assert.Empty(tls.CertificateAuthorities);
        Assert.False(tls.RequireHttps);
        Assert.Null(tls.HealthPort);
        Assert.Null(tls.LoadServerCertificate());
        Assert.Empty(tls.LoadAuthorities());
    }

    [Fact]
    public void LoadAuthorities_ReadsEveryCertificateInEveryFile()
    {
        using var first = Authority("First");
        using var second = Authority("Second");
        var path = Path.Combine(_directory, "authorities.pem");
        File.WriteAllText(path, first.ExportCertificatePem() + "\n" + second.ExportCertificatePem());

        var authorities = new ServiceTls { CertificateAuthorities = [path] }.LoadAuthorities();

        Assert.Equal(2, authorities.Count);
    }

    [Fact]
    public void LoadServerCertificate_ReadsTheCertificateWithItsKey()
    {
        var (certificate, key) = ServerSettings(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

        using var loaded = new ServiceTls { ServerCertificate = certificate, ServerKey = key }.LoadServerCertificate();

        Assert.NotNull(loaded);
        Assert.True(loaded.HasPrivateKey);
        Assert.Equal("CN=localhost", loaded.Subject);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void LoadServerCertificate_OnlyOneSet_Throws(bool certificate, bool key)
    {
        var (c, k) = ServerSettings(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceTls { ServerCertificate = certificate ? c : null, ServerKey = key ? k : " " }.LoadServerCertificate());

        Assert.Contains("go together", exception.Message);
    }

    // The messages name the settings, never a value: the key is a secret.
    [Fact]
    public void LoadServerCertificate_AnotherCertificatesKey_ThrowsWithoutTheKey()
    {
        var (certificate, _) = ServerSettings(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        var (_, other) = ServerSettings(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceTls { ServerCertificate = certificate, ServerKey = other }.LoadServerCertificate());

        Assert.Contains("Tls:ServerKey", exception.Message);
        Assert.DoesNotContain(other, exception.Message);
    }

    [Theory]
    [InlineData("not base64!", "a2V5")]
    [InlineData("bm90IGEgY2VydGlmaWNhdGU=", "a2V5")]
    public void LoadServerCertificate_ACertificateThatCantBeRead_Throws(string certificate, string key)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceTls { ServerCertificate = certificate, ServerKey = key }.LoadServerCertificate());

        Assert.Contains("Tls:ServerCertificate", exception.Message);
    }

    // A key that isn't base64, with a certificate that can be read, names the key's setting.
    [Fact]
    public void LoadServerCertificate_AKeyThatIsNotBase64_Throws()
    {
        var (certificate, _) = ServerSettings(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceTls { ServerCertificate = certificate, ServerKey = "not base64!" }.LoadServerCertificate());

        Assert.Equal("Tls:ServerKey can't be read as a base64 PKCS #8 private key.", exception.Message);
    }

    // A certificate whose key is neither ECDSA nor RSA (DSA here) says so, rather than blaming the key setting.
    [Fact]
    public void LoadServerCertificate_ACertificateWithAnotherKindOfKey_Throws()
    {
        using var issuer = RSA.Create(2048);
        var dsa = new PublicKey(
            new Oid("1.2.840.10040.4.1"), new AsnEncodedData([0x05, 0x00]), new AsnEncodedData([0x02, 0x01, 0x01]));
        var request = new CertificateRequest(new X500DistinguishedName("CN=localhost"), dsa, HashAlgorithmName.SHA256);
        using var made = request.Create(
            new X500DistinguishedName("CN=issuer"),
            X509SignatureGenerator.CreateForRSA(issuer, RSASignaturePadding.Pkcs1),
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(1),
            [1, 2, 3, 4]);

        var exception = Assert.Throws<InvalidOperationException>(() => new ServiceTls
        {
            ServerCertificate = Convert.ToBase64String(made.RawData),
            ServerKey = Convert.ToBase64String(issuer.ExportPkcs8PrivateKey()),
        }.LoadServerCertificate());

        Assert.Equal("Tls:ServerCertificate's key is neither ECDSA nor RSA.", exception.Message);
    }

    [Fact]
    public void LoadServerCertificate_AnExpiredCertificate_Throws()
    {
        var (certificate, key) = ServerSettings(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceTls { ServerCertificate = certificate, ServerKey = key }.LoadServerCertificate());

        Assert.Contains("not valid now", exception.Message);
    }

    [Fact]
    public void LoadServerCertificate_ACertificateNotYetValid_Throws()
    {
        var (certificate, key) = ServerSettings(DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(2));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceTls { ServerCertificate = certificate, ServerKey = key }.LoadServerCertificate());

        Assert.Contains("not valid now", exception.Message);
    }

    [Fact]
    public void LoadServerCertificate_AnRsaCertificate_IsRead()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var made = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

        using var loaded = new ServiceTls
        {
            ServerCertificate = Convert.ToBase64String(made.RawData),
            ServerKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()),
        }.LoadServerCertificate();

        Assert.True(loaded!.HasPrivateKey);
    }

    private static X509Certificate2 Authority(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static (string Certificate, string Key) ServerSettings(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        using var made = request.CreateSelfSigned(notBefore, notAfter);
        return (Convert.ToBase64String(made.RawData), Convert.ToBase64String(key.ExportPkcs8PrivateKey()));
    }
}
