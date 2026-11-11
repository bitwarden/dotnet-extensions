using Bitwarden.Server.Sdk.Crypto.Native;

// Generated types share their names with the public ones; alias them to tell them apart.
using NativeEncStringAlgorithm = Bitwarden.Server.Sdk.Crypto.Native.EncStringAlgorithm;
using NativeEncStringInfo = Bitwarden.Server.Sdk.Crypto.Native.EncStringInfo;
using NativeKeyIds = Bitwarden.Server.Sdk.Crypto.Native.CryptoObjectKeyIds;
using NativePublicKeyAlgorithm = Bitwarden.Server.Sdk.Crypto.Native.PublicKeyAlgorithm;
using NativePublicKeyInfo = Bitwarden.Server.Sdk.Crypto.Native.PublicKeyInfo;
using NativeSharedKeyAlgorithm = Bitwarden.Server.Sdk.Crypto.Native.UnsignedSharedKeyAlgorithm;
using NativeSharedKeyInfo = Bitwarden.Server.Sdk.Crypto.Native.UnsignedSharedKeyInfo;
using NativeVerifyingKeyAlgorithm = Bitwarden.Server.Sdk.Crypto.Native.VerifyingKeyAlgorithm;
using NativeVerifyingKeyInfo = Bitwarden.Server.Sdk.Crypto.Native.VerifyingKeyInfo;

namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// Adapts the generated UniFFI <see cref="ServerClient"/> to the public domain interfaces.
/// </summary>
/// <remarks>
/// The Rust clients are stateless and thread-safe, so one instance serves the whole process. Generated
/// exceptions are translated, keeping UniFFI types out of the public API.
/// </remarks>
internal sealed class NativeServerSdk : ICryptoParsingClient, IServerSdkInfo, IDisposable
{
    private readonly ServerClient _client;
    private readonly CryptoParsingClient _parsing;

    // Runs once, before the first ServerClient touches the native library.
    static NativeServerSdk()
    {
        NativeLibraryResolver.Register();
    }

    public NativeServerSdk()
    {
        _client = new ServerClient();
        _parsing = _client.CryptoParsing();
    }

    public string Version => _client.Version();

    public EncStringInfo InspectEncString(string encString) =>
        ToPublic(Parse(encString, _parsing.InspectEncString));

    public UnsignedSharedKeyInfo InspectUnsignedSharedKey(string sharedKey) =>
        ToPublic(Parse(sharedKey, _parsing.InspectUnsignedSharedKey));

    public CryptoObjectKeyIds InspectSymmetricKeyEnvelope(string envelope) =>
        ToPublic(Parse(envelope, _parsing.InspectSymmetricKeyEnvelope));

    public CryptoObjectKeyIds InspectLegacyCompatSymmetricKeyEnvelope(string envelope) =>
        ToPublic(Parse(envelope, _parsing.InspectLegacyCompatSymmetricKeyEnvelope));

    public CryptoObjectKeyIds InspectPasswordProtectedKeyEnvelope(string envelope) =>
        ToPublic(Parse(envelope, _parsing.InspectPasswordProtectedKeyEnvelope));

    public CryptoObjectKeyIds InspectDataEnvelope(string envelope) =>
        ToPublic(Parse(envelope, _parsing.InspectDataEnvelope));

    public CryptoObjectKeyIds InspectSignedObject(string signedObject) =>
        ToPublic(Parse(signedObject, _parsing.InspectSignedObject));

    public VerifyingKeyInfo InspectVerifyingKey(string verifyingKey) =>
        ToPublic(Parse(verifyingKey, _parsing.InspectVerifyingKey));

    public PublicKeyInfo InspectPublicKey(string publicKey) =>
        ToPublic(Parse(publicKey, _parsing.InspectPublicKey));

    public void Dispose()
    {
        _parsing.Dispose();
        _client.Dispose();
    }

    // Runs one native parse, translating its error so UniFFI types stay internal.
    private static T Parse<T>(string input, Func<string, T> parse)
    {
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            return parse(input);
        }
        catch (ServerException ex)
        {
            throw new InvalidEncryptedDataException(ex.Message, ex);
        }
    }

    private static CryptoObjectKeyIds ToPublic(NativeKeyIds ids) =>
        new(ids.ContainedKeyId, ids.EncryptedByKeyId, ids.SignedByKeyId);

    private static EncStringInfo ToPublic(NativeEncStringInfo info) =>
        new(ToPublic(info.Algorithm), info.EncryptedByKeyId);

    private static UnsignedSharedKeyInfo ToPublic(NativeSharedKeyInfo info) =>
        new(ToPublic(info.Algorithm));

    private static VerifyingKeyInfo ToPublic(NativeVerifyingKeyInfo info) =>
        new(ToPublic(info.Algorithm), info.KeyId);

    private static PublicKeyInfo ToPublic(NativePublicKeyInfo info) =>
        new(ToPublic(info.Algorithm), info.KeyId);

    private static EncStringAlgorithm ToPublic(NativeEncStringAlgorithm algorithm) => algorithm switch
    {
        NativeEncStringAlgorithm.Aes256Cbc => EncStringAlgorithm.Aes256Cbc,
        NativeEncStringAlgorithm.Aes256CbcHmac => EncStringAlgorithm.Aes256CbcHmac,
        NativeEncStringAlgorithm.Aes256Gcm => EncStringAlgorithm.Aes256Gcm,
        NativeEncStringAlgorithm.XAes256Gcm => EncStringAlgorithm.XAes256Gcm,
        NativeEncStringAlgorithm.XChaCha20Poly1305 => EncStringAlgorithm.XChaCha20Poly1305,
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
    };

    private static UnsignedSharedKeyAlgorithm ToPublic(NativeSharedKeyAlgorithm algorithm) => algorithm switch
    {
        NativeSharedKeyAlgorithm.Rsa2048OaepSha256 => UnsignedSharedKeyAlgorithm.Rsa2048OaepSha256,
        NativeSharedKeyAlgorithm.Rsa2048OaepSha1 => UnsignedSharedKeyAlgorithm.Rsa2048OaepSha1,
        NativeSharedKeyAlgorithm.Rsa2048OaepSha256HmacSha256 => UnsignedSharedKeyAlgorithm.Rsa2048OaepSha256HmacSha256,
        NativeSharedKeyAlgorithm.Rsa2048OaepSha1HmacSha256 => UnsignedSharedKeyAlgorithm.Rsa2048OaepSha1HmacSha256,
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
    };

    private static VerifyingKeyAlgorithm ToPublic(NativeVerifyingKeyAlgorithm algorithm) => algorithm switch
    {
        NativeVerifyingKeyAlgorithm.Ed25519 => VerifyingKeyAlgorithm.Ed25519,
        NativeVerifyingKeyAlgorithm.MlDsa44 => VerifyingKeyAlgorithm.MlDsa44,
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
    };

    private static PublicKeyAlgorithm ToPublic(NativePublicKeyAlgorithm algorithm) => algorithm switch
    {
        NativePublicKeyAlgorithm.RsaOaepSha1 => PublicKeyAlgorithm.RsaOaepSha1,
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
    };
}
