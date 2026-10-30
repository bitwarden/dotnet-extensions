using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Bitwarden.Server.Sdk.Common.Tests;

public sealed class InternalTlsTests : IDisposable
{
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

    private static X509Certificate2 Issue(X509Certificate2 authority)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=widget-service", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        return request.Create(authority, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(12), [1, 2, 3, 4]);
    }
}
