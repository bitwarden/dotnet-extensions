using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Bitwarden.Server.Sdk.Http.Tests;

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
        using var system = SystemChain();
        using var server = Issue(authority);

        Assert.True(InternalTls.Trusts([authority], server, system, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Trusts_NotAChainToAnotherAuthority()
    {
        using var authority = Authority("Internal");
        using var other = Authority("Other");
        using var system = SystemChain();
        using var server = Issue(authority);

        Assert.False(InternalTls.Trusts([other], server, system, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Trusts_NothingExtraWhenNoAuthorityIsNamed()
    {
        using var authority = Authority("Internal");
        using var system = SystemChain();
        using var server = Issue(authority);

        Assert.False(InternalTls.Trusts([], server, system, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Theory]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors)]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable)]
    public void Trusts_NeverRescuesAnythingButTheChain(SslPolicyErrors errors)
    {
        using var authority = Authority("Internal");
        using var system = SystemChain();
        using var server = Issue(authority);

        Assert.False(InternalTls.Trusts([authority], server, system, errors));
    }

    [Fact]
    public void Trusts_ACertificateForServers()
    {
        using var authority = Authority("Internal");
        using var system = SystemChain();
        using var server = Issue(authority, ServerAuthentication);

        Assert.True(InternalTls.Trusts([authority], server, system, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Theory]
    [InlineData(ClientAuthentication)]
    [InlineData(CodeSigning)]
    public void Trusts_NotACertificateForAnotherPurpose(string purpose)
    {
        using var authority = Authority("Internal");
        using var system = SystemChain();
        using var server = Issue(authority, purpose);

        Assert.False(InternalTls.Trusts([authority], server, system, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Trusts_NotAnExpiredCertificate()
    {
        using var authority = Authority("Internal");
        using var system = SystemChain();
        using var server = Issue(authority, notBefore: DateTimeOffset.UtcNow.AddHours(-2), notAfter: DateTimeOffset.UtcNow.AddHours(-1));

        Assert.False(InternalTls.Trusts([authority], server, system, SslPolicyErrors.RemoteCertificateChainErrors));
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

    // The server's certificate names where its issuer's revocation list is; the intermediate issuer names none for
    // itself. Checking the end certificate only, the chain is trusted; checking the intermediate too, its status is
    // unknown, so it isn't. The chain to a named authority checks as much of the chain as the system's chain was set
    // to. macOS never fetches a revocation list for a chain to a custom root, so it trusts neither.
    [Theory]
    [InlineData(X509RevocationFlag.EndCertificateOnly, true)]
    [InlineData(X509RevocationFlag.ExcludeRoot, false)]
    public async Task Trusts_ChecksRevocationOfAsMuchOfTheChainAsTheSystemsChainDoes(X509RevocationFlag flag, bool trusted)
    {
        Assert.SkipWhen(OperatingSystem.IsMacOS(), "macOS never fetches a revocation list for a chain to a custom root.");
        using var authority = Authority("Internal");
        using var intermediateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var intermediateRequest = new CertificateRequest($"CN=Revoking {Guid.NewGuid()}", intermediateKey, HashAlgorithmName.SHA256);
        intermediateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        intermediateRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        intermediateRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(intermediateRequest.PublicKey, false));
        using var issued = intermediateRequest.Create(authority, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(18), [5, 5, 5, 5]);
        using var intermediate = issued.CopyWithPrivateKey(intermediateKey);
        var revocations = new CertificateRevocationListBuilder().Build(intermediate, 1, DateTimeOffset.UtcNow.AddHours(12), HashAlgorithmName.SHA256);
        await using var lists = new IssuerServer(revocations, TimeSpan.Zero);

        using var serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var serverRequest = new CertificateRequest("CN=widget-service", serverKey, HashAlgorithmName.SHA256);
        serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        serverRequest.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([lists.Address.AbsoluteUri]));
        serverRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(intermediate, true, false));
        using var server = serverRequest.Create(intermediate, DateTimeOffset.UtcNow.AddMinutes(-30), DateTimeOffset.UtcNow.AddHours(12), [9, 9, 9, 9]);
        using var system = new X509Chain();
        system.ChainPolicy.ExtraStore.Add(issued);
        system.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        system.ChainPolicy.RevocationFlag = flag;
        system.Build(server);

        Assert.Equal(trusted, InternalTls.Trusts([authority], server, system, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    // The intermediate isn't in the system's chain, only at the address the server's certificate names for its
    // issuer. Where the system's chain may not download it, neither may the chain to a named authority. Each
    // intermediate has a name of its own: a chain may reuse one it found before by its name.
    [Fact]
    public async Task Trusts_DownloadsNothingTheSystemsChainMayNot()
    {
        using var authority = Authority("Internal");
        using var intermediate = Intermediate(authority, $"Downloaded {Guid.NewGuid()}");
        await using var issuers = new IssuerServer(intermediate.RawData, TimeSpan.Zero);
        using var server = Issue(intermediate, caIssuers: issuers.Address);
        using var system = SystemChain();
        system.ChainPolicy.DisableCertificateDownloads = true;
        system.Build(server);

        Assert.False(InternalTls.Trusts([authority], server, system, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.Equal(0, issuers.Requests);
    }

    // Where the system's chain may download an issuer, the chain to a named authority waits for it no longer than the
    // system's chain would. macOS caps a download's wait itself and ignores the chain's timeout.
    [Fact]
    public async Task Trusts_WaitsForADownloadNoLongerThanTheSystemsChain()
    {
        Assert.SkipWhen(OperatingSystem.IsMacOS(), "macOS ignores a chain's URL retrieval timeout.");
        using var authority = Authority("Internal");
        using var intermediate = Intermediate(authority, $"Slow {Guid.NewGuid()}");
        await using var issuers = new IssuerServer(intermediate.RawData, TimeSpan.FromSeconds(3));
        using var server = Issue(intermediate, caIssuers: issuers.Address);
        using var system = SystemChain();
        system.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromMilliseconds(500);

        Assert.False(InternalTls.Trusts([authority], server, system, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Trusts_NothingExtraWithoutTheSystemsChain()
    {
        using var authority = Authority("Internal");
        using var server = Issue(authority, ServerAuthentication);

        Assert.False(InternalTls.Trusts([authority], server, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Trusts_NoCertificate_IsRefused()
    {
        using var authority = Authority("Internal");

        using var system = SystemChain();

        Assert.False(InternalTls.Trusts([authority], null, system, SslPolicyErrors.RemoteCertificateChainErrors));
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

    private static X509Chain SystemChain()
    {
        var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain;
    }

    private static X509Certificate2 Intermediate(X509Certificate2 authority, string name = "Intermediate")
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        // Outlasts a certificate it issues later, which is valid for 12 hours, and ends before its authority.
        using var certificate = request.Create(authority, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(18), [5, 6, 7, 8]);
        return certificate.CopyWithPrivateKey(key);
    }

    private static X509Certificate2 Issue(
        X509Certificate2 authority,
        string? purpose = null,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null,
        Uri? caIssuers = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=widget-service", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        if (purpose is not null)
        {
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(purpose)], false));
        }

        if (caIssuers is not null)
        {
            request.CertificateExtensions.Add(new X509AuthorityInformationAccessExtension(null, [caIssuers.AbsoluteUri]));
        }

        return request.Create(
            authority,
            notBefore ?? DateTimeOffset.UtcNow.AddHours(-1),
            notAfter ?? DateTimeOffset.UtcNow.AddHours(12),
            [1, 2, 3, 4]);
    }
    [Fact]
    public void LoadServerCertificate_NeitherSet_IsNone()
    {
        Assert.Null(new InternalTls().LoadServerCertificate());
        Assert.Null(new InternalTls().HealthPort);
    }

    [Fact]
    public void LoadServerCertificate_ReadsTheCertificateWithItsKey()
    {
        var (certificate, key) = ServerSettings(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

        using var loaded = new InternalTls { ServerCertificate = certificate, ServerKey = key }.LoadServerCertificate();

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
            new InternalTls { ServerCertificate = certificate ? c : null, ServerKey = key ? k : " " }.LoadServerCertificate());

        Assert.Contains("go together", exception.Message);
    }

    // The messages name the settings, never a value: the key is a secret.
    [Fact]
    public void LoadServerCertificate_AnotherCertificatesKey_ThrowsWithoutTheKey()
    {
        var (certificate, _) = ServerSettings(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        var (_, other) = ServerSettings(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new InternalTls { ServerCertificate = certificate, ServerKey = other }.LoadServerCertificate());

        Assert.Contains("Tls:ServerKey", exception.Message);
        Assert.DoesNotContain(other, exception.Message);
    }

    [Theory]
    [InlineData("not base64!", "a2V5")]
    [InlineData("bm90IGEgY2VydGlmaWNhdGU=", "a2V5")]
    public void LoadServerCertificate_ACertificateThatCantBeRead_Throws(string certificate, string key)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new InternalTls { ServerCertificate = certificate, ServerKey = key }.LoadServerCertificate());

        Assert.Contains("Tls:ServerCertificate", exception.Message);
    }

    // A key that isn't base64, with a certificate that can be read, names the key's setting.
    [Fact]
    public void LoadServerCertificate_AKeyThatIsNotBase64_Throws()
    {
        var (certificate, _) = ServerSettings(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new InternalTls { ServerCertificate = certificate, ServerKey = "not base64!" }.LoadServerCertificate());

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

        var exception = Assert.Throws<InvalidOperationException>(() => new InternalTls
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
            new InternalTls { ServerCertificate = certificate, ServerKey = key }.LoadServerCertificate());

        Assert.Contains("not valid now", exception.Message);
    }

    [Fact]
    public void LoadServerCertificate_ACertificateNotYetValid_Throws()
    {
        var (certificate, key) = ServerSettings(DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(2));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new InternalTls { ServerCertificate = certificate, ServerKey = key }.LoadServerCertificate());

        Assert.Contains("not valid now", exception.Message);
    }

    [Fact]
    public void LoadServerCertificate_AnRsaCertificate_IsRead()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var made = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

        using var loaded = new InternalTls
        {
            ServerCertificate = Convert.ToBase64String(made.RawData),
            ServerKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()),
        }.LoadServerCertificate();

        Assert.True(loaded!.HasPrivateKey);
    }

    private static (string Certificate, string Key) ServerSettings(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        using var made = request.CreateSelfSigned(notBefore, notAfter);
        return (Convert.ToBase64String(made.RawData), Convert.ToBase64String(key.ExportPkcs8PrivateKey()));
    }

    /// <summary>
    /// Serves one certificate over plain HTTP at any path, after a delay, as an issuer's address does, and counts the
    /// requests for it.
    /// </summary>
    private sealed class IssuerServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serving;
        private int _requests;

        public IssuerServer(byte[] certificate, TimeSpan delay)
        {
            _listener.Start();
            Address = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/issuer.cer");
            _serving = Serve(certificate, delay);
        }

        public Uri Address { get; }

        public int Requests => Volatile.Read(ref _requests);

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try
            {
                await _serving;
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
            }

            _stop.Dispose();
        }

        private async Task Serve(byte[] certificate, TimeSpan delay)
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                Interlocked.Increment(ref _requests);
                var stream = client.GetStream();
                var buffer = new byte[4096];
                _ = await stream.ReadAsync(buffer, _stop.Token);
                await Task.Delay(delay, _stop.Token);
                var head = System.Text.Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Type: application/pkix-cert\r\nContent-Length: {certificate.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head, _stop.Token);
                await stream.WriteAsync(certificate, _stop.Token);
            }
        }
    }
}
