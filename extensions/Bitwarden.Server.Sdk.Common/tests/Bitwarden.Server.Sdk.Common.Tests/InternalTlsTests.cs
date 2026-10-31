using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Bitwarden.Server.Sdk.Common.Tests;

public sealed class InternalTlsTests : IDisposable
{
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";
    private const string ClientAuthentication = "1.3.6.1.5.5.7.3.2";
    private const string CodeSigning = "1.3.6.1.5.5.7.3.3";

    private readonly string _directory = Directory.CreateTempSubdirectory().FullName;

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Defaults_TrustNothingExtraAndAllowHttp()
    {
        var tls = new InternalTls();

        Assert.Empty(tls.CertificateAuthorities);
        Assert.False(tls.RequireHttps);
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

    [Fact]
    public void Trusts_WhatTheSystemTrusts()
    {
        Assert.True(InternalTls.Trusts([], null, null, SslPolicyErrors.None));
    }

    [Fact]
    public void Trusts_AChainToANamedAuthority()
    {
        using var authority = Authority("Internal");
        using var server = Issue(authority);

        Assert.True(InternalTls.Trusts([authority], server, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Trusts_NotAChainToAnotherAuthority()
    {
        using var authority = Authority("Internal");
        using var other = Authority("Other");
        using var server = Issue(authority);

        Assert.False(InternalTls.Trusts([other], server, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Trusts_NothingExtraWhenNoAuthorityIsNamed()
    {
        using var authority = Authority("Internal");
        using var server = Issue(authority);

        Assert.False(InternalTls.Trusts([], server, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Theory]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors)]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable)]
    public void Trusts_NeverRescuesAnythingButTheChain(SslPolicyErrors errors)
    {
        using var authority = Authority("Internal");
        using var server = Issue(authority);

        Assert.False(InternalTls.Trusts([authority], server, null, errors));
    }

    [Fact]
    public void Trusts_ACertificateForServers()
    {
        using var authority = Authority("Internal");
        using var server = Issue(authority, ServerAuthentication);

        Assert.True(InternalTls.Trusts([authority], server, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Theory]
    [InlineData(ClientAuthentication)]
    [InlineData(CodeSigning)]
    public void Trusts_NotACertificateForAnotherPurpose(string purpose)
    {
        using var authority = Authority("Internal");
        using var server = Issue(authority, purpose);

        Assert.False(InternalTls.Trusts([authority], server, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Trusts_NotAnExpiredCertificate()
    {
        using var authority = Authority("Internal");
        using var server = Issue(authority, notBefore: DateTimeOffset.UtcNow.AddHours(-2), notAfter: DateTimeOffset.UtcNow.AddHours(-1));

        Assert.False(InternalTls.Trusts([authority], server, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Trusts_AnIntermediateTheSystemsChainCarries()
    {
        using var authority = Authority("Internal");
        using var intermediate = Intermediate(authority);
        using var server = Issue(intermediate);
        using var system = new X509Chain();
        system.ChainPolicy.ExtraStore.Add(intermediate);
        system.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        system.Build(server);

        Assert.True(InternalTls.Trusts([authority], server, system, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(InternalTls.Trusts([authority], server, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Theory]
    [InlineData(X509RevocationMode.NoCheck, true)]
    [InlineData(X509RevocationMode.Offline, false)]
    public void Trusts_ChecksRevocationAsTheSystemsChainDoes(X509RevocationMode mode, bool trusted)
    {
        using var authority = Authority("Internal");
        using var server = Issue(authority);
        using var system = new X509Chain();
        system.ChainPolicy.RevocationMode = mode;

        Assert.Equal(trusted, InternalTls.Trusts([authority], server, system, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Trusts_NoCertificate_IsRefused()
    {
        using var authority = Authority("Internal");

        Assert.False(InternalTls.Trusts([authority], null, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Trusts_WithoutAuthorities_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => InternalTls.Trusts(null!, null, null, SslPolicyErrors.None));
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
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static X509Certificate2 Intermediate(X509Certificate2 authority)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Intermediate", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var certificate = request.Create(authority, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(12), [5, 6, 7, 8]);
        return certificate.CopyWithPrivateKey(key);
    }

    private static X509Certificate2 Issue(
        X509Certificate2 authority,
        string? purpose = null,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=widget-service", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        if (purpose is not null)
        {
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(purpose)], false));
        }

        return request.Create(
            authority,
            notBefore ?? DateTimeOffset.UtcNow.AddHours(-1),
            notAfter ?? DateTimeOffset.UtcNow.AddHours(12),
            [1, 2, 3, 4]);
    }
}
