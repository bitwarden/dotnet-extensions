using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bitwarden.Core.Json;

public class EncryptedStringConverter : JsonConverter<EncryptedString>
{
    public override EncryptedString? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            // TODO: Do better exception
            throw new JsonException("Must be a token type of string");
        }

        // TODO: Have an efficient way of creating these without allocating a string
        return EncryptedString.Parse(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, EncryptedString value, JsonSerializerOptions options)
    {
        throw new NotImplementedException("I'm just lazy.");
    }
}
