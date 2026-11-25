using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

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

    // Services calling several others hold one assertion per callee, all from the same key. Disposing one must leave
    // the others able to sign: a signer cached per key thumbprint would be shared, and disposed with the first.
    [Theory]
    [InlineData("P-256")]
    [InlineData("RSA")]
    public void Create_SameKeyAfterAnotherInstanceIsDisposed_StillSigns(string kind)
    {
        var privateKey = kind == "RSA" ? Rsa() : P256();
        var first = ClientAssertion.FromPrivateKey(privateKey);
        _ = first.Create("internal.test", Issuer, Now);
        first.Dispose();

        using var second = ClientAssertion.FromPrivateKey(privateKey);
        var (_, _, data, signature) = Parts(second.Create("internal.test", Issuer, Now));

        var publicKey = Convert.FromBase64String(second.PublicKey());
        if (kind == "RSA")
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            Assert.True(rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        else
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            Assert.True(ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        }
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

    // RFC 7517's example private keys (appendix A.2), each with its RFC 7638 thumbprint: the RSA one is RFC 7638's own
    // example (section 3.1), the P-256 one computed outside .NET.
    [Fact]
    public void KeyId_Rfc7517ExampleRsaKey_IsItsRfc7638Thumbprint()
    {
        using var rsa = RSA.Create(new RSAParameters
        {
            Modulus = Base64UrlDecode(
                "0vx7agoebGcQSuuPiLJXZptN9nndrQmbXEps2aiAFbWhM78LhWx4cbbfAAtVT86zwu1RK7aPFFxuhDR1L6tSoc_BJECPebWKRXjBZCiFV4n3oknjhMs"
                + "tn64tZ_2W-5JsGY4Hc5n9yBXArwl93lqt7_RN5w6Cf0h4QyQ5v-65YGjQR0_FDW2QvzqY368QQMicAtaSqzs8KJZgnYb9c7d0zgdAZHzu6qMQvRL5hajr"
                + "n1n91CbOpbISD08qNLyrdkt-bFTWhAI4vMQFh6WeZu0fM4lFd2NcRwr3XPksINHaQ-G_xBniIqbw0Ls1jF44-csFCur-kEgU8awapJzKnqDKgw"),
            Exponent = [1, 0, 1],
            D = Base64UrlDecode(
                "X4cTteJY_gn4FYPsXB8rdXix5vwsg1FLN5E3EaG6RJoVH-HLLKD9M7dx5oo7GURknchnrRweUkC7hT5fJLM0WbFAKNLWY2vv7B6NqXSzUvxT0_YSfqij"
                + "wp3RTzlBaCxWp4doFk5N2o8Gy_nHNKroADIkJ46pRUohsXywbReAdYaMwFs9tv8d_cPVY3i07a3t8MN6TNwm0dSawm9v47UiCl3Sk5ZiG7xojPLu4sbg1U2jx4IBTNBz"
                + "nbJSzFHK66jT8bgkuqsk0GjskDJk19Z4qwjwbsnn4j2WBii3RL-Us2lGVkY8fkFzme1z0HbIkfz0Y6mqnOYtqc0X4jfcKoAC8Q"),
            P = Base64UrlDecode(
                "83i-7IvMGXoMXCskv73TKr8637FiO7Z27zv8oj6pbWUQyLPQBQxtPVnwD20R-60eTDmD2ujnMt5PoqMrm8RfmNhVWDtjjMmCMjOpSXicFHj7XOuVIYQyqV"
                + "WlWEh6dN36GVZYk93N8Bc9vY41xy8B9RzzOGVQzXvNEvn7O0nVbfs"),
            Q = Base64UrlDecode(
                "3dfOR9cuYq-0S-mkFLzgItgMEfFzB2q3hWehMuG0oCuqnb3vobLyumqjVZQO1dIrdwgTnCdpYzBcOfW5r370AFXjiWft_NGEiovonizhKpo9VVS78TzFgx"
                + "kIdrecRezsZ-1kYd_s1qDbxtkDEgfAITAG9LUnADun4vIcb6yelxk"),
            DP = Base64UrlDecode(
                "G4sPXkc6Ya9y8oJW9_ILj4xuppu0lzi_H7VTkS8xj5SdX3coE0oimYwxIi2emTAue0UOa5dpgFGyBJ4c8tQ2VF402XRugKDTP8akYhFo5tAA77Qe_Nmtu"
                + "YZc3C3m3I24G2GvR5sSDxUyAN2zq8Lfn9EUms6rY3Ob8YeiKkTiBj0"),
            DQ = Base64UrlDecode(
                "s9lAH9fggBsoFR8Oac2R_E2gw282rT2kGOAhvIllETE1efrA6huUUvMfBcMpn8lqeW6vzznYY5SSQF7pMdC_agI3nG8Ibp1BUb0JUiraRNqUfLhcQb_d9"
                + "GF4Dh7e74WbRsobRonujTYN1xCaP6TO61jvWrX-L18txXw494Q_cgk"),
            InverseQ = Base64UrlDecode(
                "GyM_p6JrXySiz1toFgKbWV-JdI3jQ4ypu9rbMWx3rQJBfmt0FoYzgUIZEVFEcOqwemRN81zoDAaa-Bk0KWNGDjJHZDdDmFhW3AN7lI-puxk_mHZGJ11rx"
                + "yR8O55XLSe3SPmRfKwZI6yU24ZxvQKFYItdldUKGzO6Ia6zTKhAVRU"),
        });
        using var assertion = ClientAssertion.FromPrivateKey(Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()));

        Assert.Equal("NzbLsXh8uDCcd-6MNwXF4W_7noWXFZAfHkxZsRGC9Xs", assertion.KeyId);
    }

    [Fact]
    public void KeyId_Rfc7517ExampleP256Key_IsItsRfc7638Thumbprint()
    {
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Base64UrlDecode("MKBCTNIcKUSDii11ySs3526iDZ8AiTo7Tu6KPAqv7D4"),
                Y = Base64UrlDecode("4Etl6SRW2YiLUrN5vfvVHuhp7x8PxltmWWlbbM4IFyM"),
            },
            D = Base64UrlDecode("870MB6gfuTJ4HtUnUvYMyJpr5eUZNP4Bk43bVdj3eAE"),
        });
        using var assertion = ClientAssertion.FromPrivateKey(Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()));

        Assert.Equal("cn-I_WNMClehiVp51i_0VpOENW1upEerA8sEam5hn-s", assertion.KeyId);
    }

    // The assertion is exactly what an authorization server validating with Microsoft.IdentityModel expects: its type,
    // issuer, audience and lifetime, signed with the key its kid names, with no header member or claim beyond these.
    [Theory]
    [InlineData("P-256")]
    [InlineData("RSA")]
    public async Task Create_ValidatesAgainstThePublicKey(string kind)
    {
        using var assertion = ClientAssertion.FromPrivateKey(kind == "RSA" ? Rsa() : P256());
        var publicKey = Convert.FromBase64String(assertion.PublicKey());
        using var ecdsa = kind == "RSA" ? null : ECDsa.Create();
        using var rsa = kind == "RSA" ? RSA.Create() : null;
        ecdsa?.ImportSubjectPublicKeyInfo(publicKey, out _);
        rsa?.ImportSubjectPublicKeyInfo(publicKey, out _);
        SecurityKey key = ecdsa is not null ? new ECDsaSecurityKey(ecdsa) : new RsaSecurityKey(rsa!);
        // The verifier's kid comes from the public key alone, as Identity computes it, and must match the assertion's.
        key.KeyId = Base64UrlEncoder.Encode(JsonWebKeyConverter.ConvertFromSecurityKey(key).ComputeJwkThumbprint());
        Assert.Equal(key.KeyId, assertion.KeyId);
        var now = DateTimeOffset.UtcNow;

        var jwt = assertion.Create("internal.test", Issuer, now);

        var result = await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(jwt, new TokenValidationParameters
        {
            ValidIssuer = "internal.test",
            ValidAudience = Issuer,
            IssuerSigningKey = key,
            ValidAlgorithms = [assertion.Algorithm],
            ValidTypes = [ClientAssertion.JwtType],
            RequireExpirationTime = true,
            RequireSignedTokens = true,
        });
        Assert.True(result.IsValid, result.Exception?.Message);
        var token = Assert.IsType<JsonWebToken>(result.SecurityToken);
        Assert.Equal(ClientAssertion.JwtType, token.Typ);
        Assert.Equal(assertion.KeyId, token.Kid);
        Assert.Equal("internal.test", token.Subject);

        var (header, payload, _, _) = Parts(jwt);
        Assert.Equal(["alg", "kid", "typ"], header.EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["aud", "exp", "iat", "iss", "jti", "nbf", "sub"],
            payload.EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal));
        Assert.Equal(JsonValueKind.String, payload.GetProperty("aud").ValueKind);
        Assert.Equal(now.ToUnixTimeSeconds(), payload.GetProperty("nbf").GetInt64());
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

    private static string Rsa()
    {
        using var rsa = RSA.Create(2048);
        return Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
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
