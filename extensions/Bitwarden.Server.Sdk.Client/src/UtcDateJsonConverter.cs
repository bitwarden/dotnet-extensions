using System.Globalization;
using Newtonsoft.Json;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Writes every date a generated client sends as ISO 8601 UTC with a literal <c>Z</c>, as every
/// service writes its own.
/// </summary>
/// <remarks>
/// <para>
/// NSwag generates the clients for Newtonsoft.Json, which writes a <see cref="DateTimeOffset"/>
/// with its offset (<c>+00:00</c> at UTC). Each client names this converter in its generation
/// options (<c>/JsonConverters:Bitwarden.Server.Sdk.Client.UtcDateJsonConverter</c>), so a request
/// body's dates are <c>yyyy-MM-ddTHH:mm:ss</c>, the fraction to the tick with trailing zeros left
/// off, then <c>Z</c>. A local <see cref="DateTime"/> is moved to UTC; one without a zone is taken as
/// UTC.
/// </para>
/// <para>
/// Reading accepts any ISO 8601 offset, <c>Z</c> included, as Newtonsoft does.
/// </para>
/// </remarks>
public sealed class UtcDateJsonConverter : JsonConverter
{
    /// <summary>
    /// The text of an instant: UTC, the fraction trimmed, a literal <c>Z</c>.
    /// </summary>
    /// <param name="value">The instant.</param>
    /// <returns>For example <c>2026-10-11T16:07:53.8797511Z</c> or <c>2026-10-11T16:07:53Z</c>.</returns>
    public static string Text(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override bool CanConvert(Type objectType) =>
        objectType == typeof(DateTimeOffset) || objectType == typeof(DateTimeOffset?)
        || objectType == typeof(DateTime) || objectType == typeof(DateTime?);

    /// <inheritdoc />
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        switch (value)
        {
            case null:
                writer.WriteNull();
                break;
            case DateTimeOffset offset:
                writer.WriteValue(Text(offset));
                break;
            case DateTime time:
                writer.WriteValue(Text(new DateTimeOffset(Utc(time))));
                break;
            default:
                throw new JsonSerializationException($"Not a date: {value.GetType()}.");
        }
    }

    /// <inheritdoc />
    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var nullable = Nullable.GetUnderlyingType(objectType) is not null;
        if (reader.TokenType == JsonToken.Null)
        {
            return nullable ? null : throw new JsonSerializationException($"A null where {objectType} is required.");
        }

        var instant = reader.Value switch
        {
            DateTimeOffset offset => offset,
            DateTime time => new DateTimeOffset(Utc(time)),
            string text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
            _ => throw new JsonSerializationException($"Not a date: {reader.TokenType}."),
        };

        var target = Nullable.GetUnderlyingType(objectType) ?? objectType;
        return target == typeof(DateTime) ? instant.UtcDateTime : (object)instant;
    }

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value,
    };
}
