using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Reads <see cref="BulkInput{TInput}"/> from an object or an array.
/// </summary>
internal sealed class BulkInputJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(BulkInput<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);

        return (JsonConverter)Activator.CreateInstance(
            typeof(BulkInputJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()))!;
    }
}
