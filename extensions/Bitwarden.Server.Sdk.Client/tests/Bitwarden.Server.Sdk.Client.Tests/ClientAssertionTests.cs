using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// A service with a key proves itself with an assertion it signs (RFC 7523's <c>private_key_jwt</c>), never its secret;
/// one without a key sends its secret, as before.
/// </summary>
public sealed class ClientAssertionTests
{
    private const string Issuer = "https://identity.bitwarden.test";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = new(2026, 11, 8, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_P256_SignsAnES256AssertionNamingTheClientAndTheIssuer()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var assertion = ClientAssertion.FromPrivateKey(Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()));

        var jwt = assertion.Create("internal.test", Issuer, Now);

        var (header, payload, data, signature) = Parts(jwt);
        Assert.Equal("ES256", header.GetProperty("alg").GetString());
        Assert.Equal(ClientAssertion.JwtType, header.GetProperty("typ").GetString());
        Assert.Equal(assertion.KeyId, header.GetProperty("kid").GetString());
        Assert.Equal("internal.test", payload.GetProperty("iss").GetString());
        Assert.Equal("internal.test", payload.GetProperty("sub").GetString());
        Assert.Equal(Issuer, payload.GetProperty("aud").GetString());
        Assert.Equal(Now.ToUnixTimeSeconds(), payload.GetProperty("iat").GetInt64());
        Assert.Equal(Now.ToUnixTimeSeconds() + 60, payload.GetProperty("exp").GetInt64());
        Assert.True(payload.GetProperty("jti").GetString()!.Length >= 32);
        Assert.True(ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void Create_Rsa_SignsAnRS256Assertion()
    {
        using var rsa = RSA.Create(2048);
        using var assertion = ClientAssertion.FromPrivateKey(Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()));

        var (header, _, data, signature) = Parts(assertion.Create("internal.test", Issuer, Now));

        Assert.Equal("RS256", header.GetProperty("alg").GetString());
        Assert.True(rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    // Each assertion is new, so the authorization server can refuse one it has seen.
    [Fact]
    public void Create_Twice_TwoJtis()
    {
        using var assertion = ClientAssertion.FromPrivateKey(P256());

        Assert.NotEqual(
            Parts(assertion.Create("internal.test", Issuer, Now)).Payload.GetProperty("jti").GetString(),
            Parts(assertion.Create("internal.test", Issuer, Now)).Payload.GetProperty("jti").GetString());
    }

    // The kid is RFC 7638's thumbprint of the public key: checked against the RFC's own example, then against a key's.
    [Fact]
    public void KeyId_IsTheRfc7638Thumbprint()
    {
        var modulus = Base64UrlDecode(
            "0vx7agoebGcQSuuPiLJXZptN9nndrQmbXEps2aiAFbWhM78LhWx4cbbfAAtVT86zwu1RK7aPFFxuhDR1L6tSoc_BJECPebWKRXjBZCiFV4n3oknjhMs"
            + "tn64tZ_2W-5JsGY4Hc5n9yBXArwl93lqt7_RN5w6Cf0h4QyQ5v-65YGjQR0_FDW2QvzqY368QQMicAtaSqzs8KJZgnYb9c7d0zgdAZHzu6qMQvRL5hajr"
            + "n1n91CbOpbISD08qNLyrdkt-bFTWhAI4vMQFh6WeZu0fM4lFd2NcRwr3XPksINHaQ-G_xBniIqbw0Ls1jF44-csFCur-kEgU8awapJzKnqDKgw");
        Assert.Equal("NzbLsXh8uDCcd-6MNwXF4W_7noWXFZAfHkxZsRGC9Xs", RsaThumbprint(modulus, [1, 0, 1]));

        using var rsa = RSA.Create(2048);
        using var assertion = ClientAssertion.FromPrivateKey(Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()));
        var parameters = rsa.ExportParameters(false);

        Assert.Equal(RsaThumbprint(parameters.Modulus!, parameters.Exponent!), assertion.KeyId);
    }

    [Fact]
    public void PublicKey_IsTheKeysSubjectPublicKeyInfo()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var assertion = ClientAssertion.FromPrivateKey(Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()));

        Assert.Equal(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()), assertion.PublicKey());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    public void IsReadable_NotAKey_False(string value) => Assert.False(ClientAssertion.IsReadable(value));

    [Fact]
    public void IsReadable_AP384Key_False()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        Assert.False(ClientAssertion.IsReadable(Convert.ToBase64String(p384.ExportPkcs8PrivateKey())));
    }

    [Fact]
    public void IsReadable_A1024BitRsaKey_False()
    {
        using var rsa = RSA.Create(1024);

        Assert.False(ClientAssertion.IsReadable(Convert.ToBase64String(rsa.ExportPkcs8PrivateKey())));
    }

    // With a key, the request carries the assertion, addressed to the issuer Identity's discovery document names, and
    // no secret; the issuer is read once.
    [Fact]
    public async Task Provider_WithAKey_SendsAnAssertionAndNoSecret()
    {
        var identity = new FakeIdentity();
        using var provider = Provider(identity, P256());

        await provider.GetAsync(Token);
        await provider.GetContextTokenAsync(new Context.ContextAssertion(null, Guid.NewGuid(), null, "{}"), "a.person.token", Token);

        Assert.Equal(1, identity.DiscoveryReads);
        Assert.Equal(2, identity.Forms.Count);
        foreach (var form in identity.Forms)
        {
            Assert.DoesNotContain("client_secret", form.Keys);
            Assert.Equal("internal.test", form["client_id"]);
            Assert.Equal(ClientAssertion.Type, form["client_assertion_type"]);
            var payload = Parts(form["client_assertion"]).Payload;
            Assert.Equal(Issuer, payload.GetProperty("aud").GetString());
            Assert.Equal("internal.test", payload.GetProperty("sub").GetString());
        }
    }

    // Without one, the secret: services move from secrets to keys one at a time.
    [Fact]
    public async Task Provider_WithoutAKey_SendsTheSecret()
    {
        var identity = new FakeIdentity();
        using var provider = Provider(identity, string.Empty);

        await provider.GetAsync(Token);

        Assert.Equal(0, identity.DiscoveryReads);
        var form = Assert.Single(identity.Forms);
        Assert.Equal("made-up-for-tests", form["client_secret"]);
        Assert.DoesNotContain("client_assertion", form.Keys);
    }

    [Fact]
    public async Task Provider_IdentityPublishesNoIssuer_IdentityUnavailable()
    {
        var identity = new FakeIdentity { PublishedIssuer = null };
        using var provider = Provider(identity, P256());

        var failure = await Assert.ThrowsAsync<Errors.ServiceUnavailableException>(() => provider.GetAsync(Token));

        Assert.Equal(ServiceTokenProvider.Issuer, failure.Service);
        Assert.Empty(identity.Forms);
    }

    // Configured by convention: Services:<callee>:CallerKey, else the service's own Bitwarden:ClientKey; with a key no
    // secret is required, and a key that can't be read stops the service, naming the setting and never the value.
    [Fact]
    public void Options_TheServicesOwnKey_StandsInForTheSecret()
    {
        var key = P256();
        var (options, result) = Configured(new() { ["Bitwarden:ClientKey"] = key });

        Assert.Equal(key, options.CallerKey);
        Assert.Equal(string.Empty, options.CallerSecret);
        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void Options_ACalleesOwnKey_IsPreferred()
    {
        var key = P256();
        var (options, _) = Configured(new() { ["Bitwarden:ClientKey"] = P256(), ["Services:callee-service:CallerKey"] = key });

        Assert.Equal(key, options.CallerKey);
    }

    [Fact]
    public void Options_AKeyThatCantBeRead_StopsTheService()
    {
        var (_, result) = Configured(new() { ["Services:callee-service:CallerKey"] = "bm90IGEga2V5" });

        Assert.True(result.Failed);
        Assert.Contains("CallerKey", result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("bm90IGEga2V5", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_NeitherKeyNorSecret_StopsTheService()
    {
        var (_, result) = Configured(new());

        Assert.True(result.Failed);
        Assert.Contains("CallerSecret", result.FailureMessage, StringComparison.Ordinal);
    }

    private static (BitwardenClientOptions Options, ValidateOptionsResult Result) Configured(Dictionary<string, string?> settings)
    {
        settings["Services:callee-service:BaseAddress"] = "http://callee.invalid";
        settings["Services:callee-service:IdentityUri"] = "http://identity.invalid";
        settings["Services:callee-service:CallerName"] = "test";

        var options = new BitwardenClientOptions();
        Conventions.Apply(options, new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), "callee-service");

        return (options, new BitwardenClientOptionsValidator("callee-service").Validate("callee-service", options));
    }

    private static ServiceTokenProvider Provider(FakeIdentity identity, string key) =>
        new(
            new SingleClientFactory(identity),
            new BitwardenClientOptions
            {
                BaseAddress = "http://callee.invalid",
                IdentityUri = "http://identity.invalid",
                CallerName = "test",
                CallerSecret = "made-up-for-tests",
                CallerKey = key,
                Audience = "callee-service",
                Scopes = ["callee.read"],
            },
            NullLogger<ServiceTokenProvider>.Instance,
            TimeProvider.System);

    private static string P256()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey());
    }

    private static string RsaThumbprint(byte[] modulus, byte[] exponent) =>
        Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{{\"e\":\"{Base64Url(exponent)}\",\"kty\":\"RSA\",\"n\":\"{Base64Url(modulus)}\"}}")));

    private static (JsonElement Header, JsonElement Payload, byte[] Data, byte[] Signature) Parts(string jwt)
    {
        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);

        return (
            JsonDocument.Parse(Base64UrlDecode(parts[0])).RootElement,
            JsonDocument.Parse(Base64UrlDecode(parts[1])).RootElement,
            Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
            Base64UrlDecode(parts[2]));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '='));
    }

    /// <summary>
    /// Identity: its discovery document, and a token for every form posted.
    /// </summary>
    private sealed class FakeIdentity : HttpMessageHandler
    {
        public string? PublishedIssuer { get; init; } = Issuer;

        public int DiscoveryReads { get; private set; }

        public List<Dictionary<string, string>> Forms { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("http://identity.invalid/.well-known/openid-configuration", request.RequestUri!.ToString());
                DiscoveryReads++;
                return Json(PublishedIssuer is null ? "{}" : $$"""{"issuer":"{{PublishedIssuer}}"}""");
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Forms.Add(body.Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => Uri.UnescapeDataString(pair[1].Replace('+', ' '))));
            return Json("""{"access_token":"a-token","expires_in":300,"token_type":"Bearer"}""");
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
