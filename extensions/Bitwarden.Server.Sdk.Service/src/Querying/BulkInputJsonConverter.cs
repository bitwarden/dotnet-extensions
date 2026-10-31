using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <inheritdoc cref="BulkInputJsonConverterFactory"/>
internal sealed class BulkInputJsonConverter<TInput> : JsonConverter<BulkInput<TInput>>
{
    public override BulkInput<TInput>? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            return new BulkInput<TInput>
            {
                Each = JsonSerializer.Deserialize<List<TInput>>(ref reader, options) ?? [],
            };
        }

        return new BulkInput<TInput>
        {
            Every = JsonSerializer.Deserialize<TInput>(ref reader, options),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        BulkInput<TInput> value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Each is { } each)
        {
            JsonSerializer.Serialize(writer, each, options);

            return;
        }

        JsonSerializer.Serialize(writer, value.Every, options);
    }
}
