using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// A service's proof of who it is at the token endpoint, signed with its own private key: RFC 7523's JWT client
/// authentication, OpenID Connect's <c>private_key_jwt</c>. Sent in place of a client secret.
/// </summary>
/// <remarks>
/// <para>
/// The assertion is a JWT of type <see cref="JwtType"/> (as <c>draft-ietf-oauth-rfc7523bis</c> types it), naming the client as both <c>iss</c> and <c>sub</c>, the authorization server's
/// issuer as <c>aud</c>, a unique <c>jti</c>, and living <see cref="Lifetime"/>. It is sent as <c>client_assertion</c>
/// with <c>client_assertion_type</c> <see cref="Type"/>. The authorization server holds only the public key, so a copy
/// of its registry can't be used to call anybody, and it refuses an assertion it has seen before.
/// </para>
/// <para>
/// The key is a PKCS#8 private key, base64: P-256 signs ES256, RSA (2048 bits or more) RS256. Its <c>kid</c> is its
/// RFC 7638 JWK thumbprint, so the server can pick the key among a client's registered ones while a key is rotated.
/// Nothing here logs or keeps the key outside the instance.
/// </para>
/// </remarks>
public sealed class ClientAssertion : IDisposable
{
    /// <summary>
    /// RFC 7523's <c>client_assertion_type</c>.
    /// </summary>
    public const string Type = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    /// <summary>
    /// The assertion's JOSE <c>typ</c>.
    /// </summary>
    public const string JwtType = "client-authentication+jwt";

    /// <summary>
    /// How long an assertion is good for: one request's worth, so a copy is worth little.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly RSA? _rsa;
    private readonly ECDsa? _ecdsa;
    private readonly string _keyId;

    private ClientAssertion(RSA? rsa, ECDsa? ecdsa)
    {
        _rsa = rsa;
        _ecdsa = ecdsa;
        _keyId = Thumbprint(rsa, ecdsa);
    }

    /// <summary>
    /// The JWS algorithm this key signs with: <c>ES256</c> or <c>RS256</c>.
    /// </summary>
    public string Algorithm => _ecdsa is not null ? "ES256" : "RS256";

    /// <summary>
    /// The key's RFC 7638 thumbprint, base64url: its <c>kid</c>.
    /// </summary>
    public string KeyId => _keyId;

    /// <summary>
    /// Reads a private key.
    /// </summary>
    /// <param name="privateKey">PKCS#8, base64: P-256 or RSA of 2048 bits or more.</param>
    /// <returns>What signs assertions with it.</returns>
    /// <exception cref="FormatException">The value isn't such a key. The message never includes it.</exception>
    public static ClientAssertion FromPrivateKey(string privateKey)
    {
        byte[] bytes;

        try
        {
            bytes = Convert.FromBase64String(privateKey.Trim());
        }
        catch (FormatException)
        {
            throw new FormatException("The client key is not base64.");
        }

        // Decoded once and zeroed once, whichever kind of key it turns out to be.
        try
        {
            return FromPkcs8(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static ClientAssertion FromPkcs8(byte[] bytes)
    {
        try
        {
            var ecdsa = ECDsa.Create();

            try
            {
                ecdsa.ImportPkcs8PrivateKey(bytes, out _);

                if (ecdsa.ExportParameters(includePrivateParameters: false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
                {
                    throw new FormatException("The client key is an elliptic curve key on a curve other than P-256.");
                }

                return new ClientAssertion(null, ecdsa);
            }
            catch
            {
                ecdsa.Dispose();
                throw;
            }
        }
        catch (CryptographicException)
        {
            // Not an elliptic curve key: RSA, or nothing.
        }

        var rsa = RSA.Create();

        try
        {
            rsa.ImportPkcs8PrivateKey(bytes, out _);
        }
        catch (CryptographicException)
        {
            rsa.Dispose();
            throw new FormatException("The client key is not a PKCS#8 P-256 or RSA private key.");
        }

        if (rsa.KeySize < 2048)
        {
            rsa.Dispose();
            throw new FormatException("The client key is an RSA key of fewer than 2048 bits.");
        }

        return new ClientAssertion(rsa, null);
    }

    /// <summary>
    /// Whether a value is a private key <see cref="FromPrivateKey"/> reads.
    /// </summary>
    /// <param name="privateKey">The configured value.</param>
    /// <returns>False for anything else, a blank value included.</returns>
    public static bool IsReadable(string? privateKey)
    {
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            return false;
        }

        try
        {
            using var _ = FromPrivateKey(privateKey);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// The public half, base64 SubjectPublicKeyInfo: what the authorization server registers for the client.
    /// </summary>
    public string PublicKey() =>
        Convert.ToBase64String(_ecdsa?.ExportSubjectPublicKeyInfo() ?? _rsa!.ExportSubjectPublicKeyInfo());

    /// <summary>
    /// Signs a new assertion.
    /// </summary>
    /// <param name="clientId">The client, <c>iss</c> and <c>sub</c>.</param>
    /// <param name="audience">The authorization server's issuer, <c>aud</c>.</param>
    /// <param name="now">When it is issued.</param>
    /// <returns>The compact JWT. A credential: never logged.</returns>
    public string Create(string clientId, string audience, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);

        var issued = now.ToUnixTimeSeconds();
        var header = Json(writer =>
        {
            writer.WriteString("alg", Algorithm);
            writer.WriteString("typ", JwtType);
            writer.WriteString("kid", _keyId);
        });
        var payload = Json(writer =>
        {
            writer.WriteString("iss", clientId);
            writer.WriteString("sub", clientId);
            writer.WriteString("aud", audience);
            writer.WriteString("jti", Base64Url(RandomNumberGenerator.GetBytes(32)));
            writer.WriteNumber("iat", issued);
            writer.WriteNumber("nbf", issued);
            writer.WriteNumber("exp", issued + (long)Lifetime.TotalSeconds);
        });

        var signed = $"{Base64Url(header)}.{Base64Url(payload)}";
        var data = Encoding.ASCII.GetBytes(signed);
        var signature = _ecdsa is not null
            ? _ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
            : _rsa!.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return $"{signed}.{Base64Url(signature)}";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _rsa?.Dispose();
        _ecdsa?.Dispose();
    }

    /// <summary>
    /// RFC 7638: the SHA-256 of the required members of the public JWK, in lexicographic order, without spaces.
    /// </summary>
    private static string Thumbprint(RSA? rsa, ECDsa? ecdsa)
    {
        string canonical;

        if (ecdsa is not null)
        {
            var parameters = ecdsa.ExportParameters(includePrivateParameters: false);
            canonical = $"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{Base64Url(parameters.Q.X!)}\",\"y\":\"{Base64Url(parameters.Q.Y!)}\"}}";
        }
        else
        {
            var parameters = rsa!.ExportParameters(includePrivateParameters: false);
            canonical = $"{{\"e\":\"{Base64Url(parameters.Exponent!)}\",\"kty\":\"RSA\",\"n\":\"{Base64Url(parameters.Modulus!)}\"}}";
        }

        return Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>
    /// A JSON object, written without reflection, so a trimmed or ahead-of-time compiled host can sign.
    /// </summary>
    private static byte[] Json(Action<Utf8JsonWriter> members)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            members(writer);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
