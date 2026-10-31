using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Reads a <see cref="Query"/> from the JSON Logic object a caller sends.
/// </summary>
/// <remarks>
/// <para>
/// Parsed here rather than after binding, so an expression using an operator the standards do
/// not permit is refused with the rest of the request's validation errors rather than later,
/// from somewhere further in.
/// </para>
/// <para>
/// Write-only in one direction: a query is something a caller sends and a service never
/// returns, so serialising one would only ever happen by mistake and says so.
/// </para>
/// </remarks>
internal sealed class QueryJsonConverter : JsonConverter<Query>
{
    public override Query Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);

        return Query.Parse(document.RootElement.Clone());
    }

    public override void Write(Utf8JsonWriter writer, Query value, JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "A query is something a caller sends, never something a service returns.");
}
