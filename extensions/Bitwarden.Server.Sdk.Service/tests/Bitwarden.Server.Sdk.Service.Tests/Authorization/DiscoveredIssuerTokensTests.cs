using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// Another issuer's access token, verified as a bearer handler pointed at that issuer verifies it, against a stand-in
/// for its discovery document and keys.
/// </summary>
public sealed class DiscoveredIssuerTokensTests : IDisposable
{
    private const string Issuer = "http://localhost";

    private readonly RSA _first = RSA.Create(2048);
    private readonly RSA _rolled = RSA.Create(2048);
    private readonly Discovery _discovery;
    private readonly HttpClient _http;
    private readonly DiscoveredIssuerTokens _tokens;

    public DiscoveredIssuerTokensTests()
    {
        _discovery = new Discovery(Issuer) { Key = new RsaSecurityKey(_first) { KeyId = "first" } };
        _http = new HttpClient(_discovery);
        _tokens = new DiscoveredIssuerTokens(new Uri("http://issuer.invalid:33656/"), _http);
    }

    public void Dispose()
    {
        _http.Dispose();
        _discovery.Dispose();
        _first.Dispose();
        _rolled.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ATokenTheIssuerSigned_Valid_WithItsClaims()
    {
        var result = await _tokens.ValidateAsync(Mint(_first, "first"), Token);

        Assert.True(result.IsValid);
        Assert.Equal("4e907f5d-0000-4000-8000-000000000485", result.ClaimsIdentity.FindFirst("sub")?.Value);
        Assert.IsType<JsonWebToken>(result.SecurityToken);
    }

    [Fact]
    public async Task SignedWithAKeyNotPublished_Invalid()
    {
        Assert.False((await _tokens.ValidateAsync(Mint(_rolled, "first"), Token)).IsValid);
    }

    [Fact]
    public async Task AnotherIssuer_Invalid()
    {
        Assert.False((await _tokens.ValidateAsync(Mint(_first, "first", issuer: "https://other.invalid"), Token)).IsValid);
    }

    [Fact]
    public async Task Expired_Invalid()
    {
        Assert.False((await _tokens.ValidateAsync(Mint(_first, "first", expires: DateTime.UtcNow.AddMinutes(-10)), Token)).IsValid);
    }

    [Fact]
    public async Task NotAnAccessToken_Invalid()
    {
        Assert.False((await _tokens.ValidateAsync(Mint(_first, "first", type: "JWT"), Token)).IsValid);
    }

    // The issuer rolls its key: a token naming the new key has the keys read again, and verifies once they have been.
    [Fact]
    public async Task TheIssuerRollsItsKey_KeysReadAgain()
    {
        Assert.True((await _tokens.ValidateAsync(Mint(_first, "first"), Token)).IsValid);
        var reads = _discovery.KeyReads;
        _discovery.Key = new RsaSecurityKey(_rolled) { KeyId = "rolled" };
        var rolled = Mint(_rolled, "rolled");

        var valid = false;
        for (var attempt = 0; attempt < 50 && !valid; attempt++)
        {
            valid = (await _tokens.ValidateAsync(rolled, Token)).IsValid;
            if (!valid)
            {
                await Task.Delay(20, Token);
            }
        }

        Assert.True(valid);
        Assert.True(_discovery.KeyReads > reads);
    }

    private static string Mint(RSA key, string keyId, string issuer = Issuer, DateTime? expires = null, string type = "at+jwt") =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Subject = new ClaimsIdentity([new Claim("sub", "4e907f5d-0000-4000-8000-000000000485")]),
            IssuedAt = DateTime.UtcNow.AddHours(-1),
            NotBefore = DateTime.UtcNow.AddHours(-1),
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            TokenType = type,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(key) { KeyId = keyId }, SecurityAlgorithms.RsaSha256),
        });

    /// <summary>
    /// The issuer's discovery document and its current key.
    /// </summary>
    private sealed class Discovery(string issuer) : HttpMessageHandler
    {
        public required RsaSecurityKey Key { get; set; }

        public int KeyReads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            string body;
            if (path == "/.well-known/openid-configuration")
            {
                body = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["issuer"] = issuer,
                    ["jwks_uri"] = "http://issuer.invalid:33656/.well-known/openid-configuration/jwks",
                });
            }
            else if (path == "/.well-known/openid-configuration/jwks")
            {
                KeyReads++;
                var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(Key);
                body = JsonSerializer.Serialize(new
                {
                    keys = new[] { new { kty = jwk.Kty, use = "sig", kid = jwk.Kid, e = jwk.E, n = jwk.N, alg = "RS256" } },
                });
            }
            else
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
