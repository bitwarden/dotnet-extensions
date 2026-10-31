using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Every date a service writes is ISO 8601, in UTC, with a literal <c>Z</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>System.Text.Json</c> writes a <see cref="DateTimeOffset"/> with its offset, so an instant at
/// offset zero comes out <c>+00:00</c>, and a <see cref="DateTime"/> as its kind says (<c>Z</c>,
/// an offset, or nothing). These converters write the instant in UTC instead:
/// <c>yyyy-MM-ddTHH:mm:ss</c>, the fraction to the tick with trailing zeros left off (none for a
/// whole second), then <c>Z</c>. Which offset a caller sent is not kept anywhere, so it is not
/// answered either; a client that needs another text translates it itself.
/// </para>
/// <para>
/// Reading is unchanged: any ISO 8601 offset, <c>Z</c> included. A <see cref="DateTime"/> read
/// without a zone has no instant of its own; it is taken as UTC, the only time a service keeps.
/// </para>
/// </remarks>
public static class UtcDates
{
    /// <summary>
    /// The text of an instant: UTC, the fraction trimmed, a literal <c>Z</c>.
    /// </summary>
    /// <param name="value">The instant.</param>
    /// <returns>For example <c>2026-10-11T16:07:53.8797511Z</c>, <c>2026-10-11T16:07:53.88Z</c>, <c>2026-10-11T16:07:53Z</c>.</returns>
    public static string Text(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// The text of a <see cref="DateTime"/>: a local time moved to UTC, one without a zone taken as UTC.
    /// </summary>
    /// <param name="value">The time.</param>
    /// <returns>As <see cref="Text(DateTimeOffset)"/>.</returns>
    public static string Text(DateTime value) => Text(new DateTimeOffset(AsUtc(value)));

    /// <summary>
    /// A time as UTC: a local one converted, one without a zone taken as UTC already.
    /// </summary>
    /// <param name="value">The time.</param>
    /// <returns>The same instant, of kind <see cref="DateTimeKind.Utc"/>.</returns>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value,
    };

    /// <summary>
    /// Adds the converters to a service's JSON options.
    /// </summary>
    /// <param name="options">The options.</param>
    public static void Apply(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Converters.Add(new OffsetConverter());
        options.Converters.Add(new DateTimeConverter());
    }

    /// <summary>
    /// Writes a <see cref="DateTimeOffset"/> as <see cref="Text(DateTimeOffset)"/>.
    /// </summary>
    public sealed class OffsetConverter : JsonConverter<DateTimeOffset>
    {
        /// <inheritdoc />
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDateTimeOffset();

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(Text(value));
        }

        /// <inheritdoc />
        public override DateTimeOffset ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        /// <inheritdoc />
        public override void WriteAsPropertyName(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WritePropertyName(Text(value));
        }
    }

    /// <summary>
    /// Writes a <see cref="DateTime"/> as <see cref="Text(DateTime)"/>.
    /// </summary>
    public sealed class DateTimeConverter : JsonConverter<DateTime>
    {
        /// <inheritdoc />
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            AsUtc(reader.GetDateTime());

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(Text(value));
        }
    }
}
