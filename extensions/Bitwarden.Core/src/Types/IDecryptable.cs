namespace Bitwarden.Core;

public interface IDecryptable<TKey>
{
    byte[] Decrypt(TKey key);
}
