using Bitwarden.Server.Sdk.Crypto.Native;

namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// Adapts the generated UniFFI <see cref="ServerClient"/> to the public domain interfaces.
/// </summary>
/// <remarks>
/// The Rust client is stateless and thread-safe, so one instance serves the whole process. Generated
/// exceptions are translated, keeping UniFFI types out of the public API.
/// </remarks>
internal sealed class NativeServerSdk : IEncryptionKeyIdReader, IServerSdkInfo, IDisposable
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

    public void Dispose()
    {
        _client.Dispose();
    }
}
