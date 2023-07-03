using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bitwarden.Core.Json;

public sealed class SymmetricCryptoKeyConverter : JsonConverter<SymmetricCryptoKey>
{
    public override SymmetricCryptoKey? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Must be a string token.");
        }

        return SymmetricCryptoKey.Parse(reader.ValueSpan);
    }

    public override void Write(Utf8JsonWriter writer, SymmetricCryptoKey value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
