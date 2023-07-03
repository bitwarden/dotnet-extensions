using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Bitwarden.Core;

public static class DecryptableExtensions
{
    public static string DecryptToString<TKey>(this IDecryptable<TKey> decryptable, TKey key)
        => Encoding.UTF8.GetString(decryptable.Decrypt(key));

    public static T? Decrypt<TKey, T>(this IDecryptable<TKey> decryptable, TKey key, JsonTypeInfo<T> jsonTypeInfo)
        => JsonSerializer.Deserialize(decryptable.Decrypt(key), jsonTypeInfo);
}
