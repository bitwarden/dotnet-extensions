namespace Bitwarden.Core;

public sealed class Payload
{
    public Payload(SymmetricCryptoKey encryptionKey)
    {
        EncryptionKey = encryptionKey;
    }

    public SymmetricCryptoKey EncryptionKey { get; }
}
