using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Writes and reads <see cref="CallerCredential"/> by its ALL_CAPS name.
/// </summary>
public sealed class CallerCredentialJsonConverter : JsonConverter<CallerCredential>
{
    /// <inheritdoc />
    public override CallerCredential Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && CallerContextHeader.Credential(reader.GetString()!) is { } credential
            ? credential
            : throw new JsonException("Not USER, ORGANIZATION, MACHINE or SYSTEM.");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CallerCredential value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(CallerContextHeader.Name(value));
    }
}
