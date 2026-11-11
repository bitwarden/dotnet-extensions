using Bitwarden.Server.Sdk.Crypto.Native;

// Generated records share their names with the public ones; alias them to tell them apart.
using NativeEncStringFamily = Bitwarden.Server.Sdk.Crypto.Native.EncStringFamily;
using NativeEncStringInfo = Bitwarden.Server.Sdk.Crypto.Native.EncStringInfo;
using NativeKeyIds = Bitwarden.Server.Sdk.Crypto.Native.CryptoObjectKeyIds;

namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// Adapts the generated UniFFI <see cref="ServerClient"/> to the public domain interfaces.
/// </summary>
/// <remarks>
/// The Rust client is stateless and thread-safe, so one instance serves the whole process. Generated
/// exceptions are translated, keeping UniFFI types out of the public API.
/// </remarks>
internal sealed class NativeServerSdk : IEncryptionKeyIdReader, ICryptoObjectInspector, IServerSdkInfo, IDisposable
{
    private readonly ServerClient _client = new();

    // Runs once, before the first ServerClient touches the native library.
    static NativeServerSdk()
    {
        NativeLibraryResolver.Register();
    }

    public string Version => _client.Version();

    public string? FromEncString(string encString)
    {
        ArgumentNullException.ThrowIfNull(encString);

        try
        {
            return _client.GetEncryptedByKeyIdEncstring(encString);
        }
        catch (ServerException ex)
        {
            throw new InvalidEncryptedDataException(ex.Message, ex);
        }
    }

    public string? FromSymmetricKeyEnvelope(string envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        try
        {
            return _client.GetEncryptedByKeyIdSymmetrickeyenvelope(envelope);
        }
        catch (ServerException ex)
        {
            throw new InvalidEncryptedDataException(ex.Message, ex);
        }
    }

    public EncStringInfo InspectEncString(string encString) =>
        ToPublic(Inspect(encString, _client.InspectEncString));

    public EncStringInfo InspectUnsignedSharedKey(string sharedKey) =>
        ToPublic(Inspect(sharedKey, _client.InspectUnsignedSharedKey));

    public CryptoObjectKeyIds InspectSymmetricKeyEnvelope(string envelope) =>
        ToPublic(Inspect(envelope, _client.InspectSymmetricKeyEnvelope));

    public CryptoObjectKeyIds InspectLegacyCompatSymmetricKeyEnvelope(string envelope) =>
        ToPublic(Inspect(envelope, _client.InspectLegacyCompatSymmetricKeyEnvelope));

    public CryptoObjectKeyIds InspectPasswordProtectedKeyEnvelope(string envelope) =>
        ToPublic(Inspect(envelope, _client.InspectPasswordProtectedKeyEnvelope));

    public CryptoObjectKeyIds InspectDataEnvelope(string envelope) =>
        ToPublic(Inspect(envelope, _client.InspectDataEnvelope));

    public CryptoObjectKeyIds InspectSignedObject(string signedObject) =>
        ToPublic(Inspect(signedObject, _client.InspectSignedObject));

    public CryptoObjectKeyIds InspectSignedPublicKey(string signedPublicKey) =>
        ToPublic(Inspect(signedPublicKey, _client.InspectSignedPublicKey));

    public CryptoObjectKeyIds InspectVerifyingKey(string verifyingKey) =>
        ToPublic(Inspect(verifyingKey, _client.InspectVerifyingKey));

    public CryptoObjectKeyIds InspectPublicKey(string publicKey) =>
        ToPublic(Inspect(publicKey, _client.InspectPublicKey));

    public CryptoObjectKeyIds InspectSignedSecurityState(string signedSecurityState) =>
        ToPublic(Inspect(signedSecurityState, _client.InspectSignedSecurityState));

    public void Dispose()
    {
        _client.Dispose();
    }

    // Runs one native inspection, translating its error so UniFFI types stay internal.
    private static T Inspect<T>(string input, Func<string, T> inspect)
    {
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            return inspect(input);
        }
        catch (ServerException ex)
        {
            throw new InvalidEncryptedDataException(ex.Message, ex);
        }
    }

    private static CryptoObjectKeyIds ToPublic(NativeKeyIds ids) =>
        new(ids.ContainedKeyId, ids.EncryptedByKeyId, ids.SignedByKeyId);

    private static EncStringInfo ToPublic(NativeEncStringInfo info) =>
        new(info.EncryptionType, ToPublic(info.Family), info.EncryptedByKeyId);

    private static EncStringFamily ToPublic(NativeEncStringFamily family) => family switch
    {
        NativeEncStringFamily.Symmetric => EncStringFamily.Symmetric,
        NativeEncStringFamily.Asymmetric => EncStringFamily.Asymmetric,
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown EncString family."),
    };
}
