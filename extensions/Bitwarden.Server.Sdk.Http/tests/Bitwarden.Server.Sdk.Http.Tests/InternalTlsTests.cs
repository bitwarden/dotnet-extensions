using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Bitwarden.Server.Sdk.Http.Tests;

public sealed class InternalTlsTests : IDisposable
{
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";
    private const string ClientAuthentication = "1.3.6.1.5.5.7.3.2";
    private const string CodeSigning = "1.3.6.1.5.5.7.3.3";
    private const string Host = "widget-service";

    private readonly string _directory = Directory.CreateTempSubdirectory().FullName;

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Defaults_NameNoAuthority()
    {
        var tls = new InternalTls();

        Assert.Empty(tls.CertificateAuthorities);
        Assert.Empty(tls.LoadAuthorities());
        Assert.Equal("Tls", InternalTls.Section);
    }

    [Fact]
    public void LoadAuthorities_ReadsEveryCertificateInEveryFile()
    {
        using var first = Authority("First");
        using var second = Authority("Second");
        using var third = Authority("Third");
        var both = Write("both.pem", first.ExportCertificatePem() + "\n" + second.ExportCertificatePem());
        var one = Write("one.pem", third.ExportCertificatePem());

        var authorities = new InternalTls { CertificateAuthorities = [both, " ", one] }.LoadAuthorities();

        Assert.Equal(3, authorities.Count);
    }

    [Fact]
    public void LoadAuthorities_AMissingFile_Throws()
    {
        var path = Path.Combine(_directory, "missing.pem");

        var exception = Assert.Throws<InvalidOperationException>(() => new InternalTls { CertificateAuthorities = [path] }.LoadAuthorities());

        Assert.Contains(path, exception.Message);
    }

    [Fact]
    public void LoadAuthorities_AFileWithNoCertificate_Throws()
    {
        var path = Write("empty.pem", "not a certificate");

        var exception = Assert.Throws<InvalidOperationException>(() => new InternalTls { CertificateAuthorities = [path] }.LoadAuthorities());

        Assert.Contains(path, exception.Message);
    }

    [Fact]
    public void LoadAuthorities_AFileThatIsNotPem_Throws()
    {
        var path = Write("broken.pem", "-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----\n");

        var exception = Assert.Throws<InvalidOperationException>(() => new InternalTls { CertificateAuthorities = [path] }.LoadAuthorities());

        Assert.Contains(path, exception.Message);
    }

    [Fact]
    public void LoadAuthorities_ADirectory_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new InternalTls { CertificateAuthorities = [_directory] }.LoadAuthorities());

        Assert.Contains(_directory, exception.Message);
    }

    [Fact]
    public void LoadAuthorities_APathThatIsNotAPath_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new InternalTls { CertificateAuthorities = ["ca\0.pem"] }.LoadAuthorities());
    }

    // A server whose certificate chains to a named authority is trusted, and the call reaches it.
    [Fact]
    public async Task ChainPolicy_TrustsAChainToANamedAuthority()
    {
        using var authority = Authority("Internal");
        using var certificate = Issue(authority);

        Assert.Equal(HttpStatusCode.OK, await CallAsync([authority], TlsServer.Serving(certificate)));
    }

    // An intermediate the server sends completes the chain; none is downloaded.
    [Fact]
    public async Task ChainPolicy_TrustsAnIntermediateTheServerSends()
    {
        using var authority = Authority("Internal");
        using var intermediate = Intermediate(authority);
        using var certificate = Issue(intermediate);

        Assert.Equal(HttpStatusCode.OK, await CallAsync([authority], TlsServer.Serving(certificate, intermediate)));
    }

    // Only the named authorities are trusted: a certificate from another authority isn't, nor is one through an
    // intermediate the server doesn't send.
    [Fact]
    public async Task ChainPolicy_RefusesAChainToAnotherAuthority()
    {
        using var authority = Authority("Internal");
        using var other = Authority("Other");
        using var intermediate = Intermediate(authority);
        using var fromOther = Issue(other);
        using var throughIntermediate = Issue(intermediate);

        await AssertRefusedAsync([authority], TlsServer.Serving(fromOther));
        await AssertRefusedAsync([authority], TlsServer.Serving(throughIntermediate));
    }

    // The system's roots are never trusted, and nothing is checked for revocation or downloaded.
    [Fact]
    public void ChainPolicy_TrustsTheNamedAuthoritiesAlone()
    {
        using var authority = Authority("Internal");

        var policy = InternalTls.ChainPolicy([authority]);

        Assert.Equal(X509ChainTrustMode.CustomRootTrust, policy.TrustMode);
        Assert.Same(authority, Assert.Single(policy.CustomTrustStore));
        Assert.Equal(X509RevocationMode.NoCheck, policy.RevocationMode);
        Assert.True(policy.DisableCertificateDownloads);
    }

    // A certificate for another name than the one called is refused.
    [Fact]
    public async Task ChainPolicy_RefusesAnotherName()
    {
        using var authority = Authority("Internal");
        using var certificate = Issue(authority, name: "other-service");

        await AssertRefusedAsync([authority], TlsServer.Serving(certificate));
    }

    // A certificate allowed only for another purpose than server authentication is refused: SslStream asks for it.
    [Theory]
    [InlineData(ClientAuthentication)]
    [InlineData(CodeSigning)]
    public async Task ChainPolicy_RefusesACertificateForAnotherPurpose(string purpose)
    {
        using var authority = Authority("Internal");
        using var certificate = Issue(authority, purpose);

        await AssertRefusedAsync([authority], TlsServer.Serving(certificate));
    }

    [Fact]
    public async Task ChainPolicy_TrustsACertificateForServers()
    {
        using var authority = Authority("Internal");
        using var certificate = Issue(authority, ServerAuthentication);

        Assert.Equal(HttpStatusCode.OK, await CallAsync([authority], TlsServer.Serving(certificate)));
    }

    [Fact]
    public async Task ChainPolicy_RefusesAnExpiredCertificate()
    {
        using var authority = Authority("Internal");
        using var certificate = Issue(authority, notBefore: DateTimeOffset.UtcNow.AddHours(-2), notAfter: DateTimeOffset.UtcNow.AddHours(-1));

        await AssertRefusedAsync([authority], TlsServer.Serving(certificate));
    }

    [Fact]
    public void ChainPolicy_WithoutAuthorities_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => InternalTls.ChainPolicy([]));

        Assert.Contains("Tls:CertificateAuthorities", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Calls <see cref="Host"/>, served <paramref name="served"/>, with a client given the chain policy for
    /// <paramref name="authorities"/>.
    /// </summary>
    private static async Task<HttpStatusCode> CallAsync(X509Certificate2Collection authorities, SslStreamCertificateContext served)
    {
        using var server = new TlsServer(served);
        using var invoker = new HttpMessageInvoker(server.Client(InternalTls.ChainPolicy(authorities)));
        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"https://{Host}:{server.Port}/"), TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private static async Task AssertRefusedAsync(X509Certificate2Collection authorities, SslStreamCertificateContext served)
    {
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => CallAsync(authorities, served));

        Assert.IsType<AuthenticationException>(exception.InnerException);
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
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

    private static X509Certificate2 Intermediate(X509Certificate2 authority, string name = "Intermediate")
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        // Outlasts a certificate it issues later, which is valid for 12 hours, and ends before its authority.
        using var certificate = request.Create(authority, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(18), [5, 6, 7, 8]);
        using var withKey = certificate.CopyWithPrivateKey(key);
        return Reloaded(withKey);
    }

    /// <summary>
    /// A server certificate, with its key, for <paramref name="name"/>.
    /// </summary>
    private static X509Certificate2 Issue(
        X509Certificate2 authority,
        string? purpose = null,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null,
        string name = Host)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(name);
        request.CertificateExtensions.Add(names.Build());
        if (purpose is not null)
        {
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(purpose)], false));
        }

        using var certificate = request.Create(
            authority,
            notBefore ?? DateTimeOffset.UtcNow.AddHours(-1),
            notAfter ?? DateTimeOffset.UtcNow.AddHours(12),
            [1, 2, 3, 4]);
        using var withKey = certificate.CopyWithPrivateKey(key);
        return Reloaded(withKey);
    }

    /// <summary>
    /// Exported and read back, so SslStream can use the key on every operating system.
    /// </summary>
    private static X509Certificate2 Reloaded(X509Certificate2 certificate) =>
        X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), null);
}
