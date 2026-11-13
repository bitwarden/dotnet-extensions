using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// <see cref="UtcDates.OffsetConverter"/> on its own: on a member it names, and for a date used as a map's key,
/// written as UTC with a <c>Z</c> and read back as the same instant.
/// </summary>
public sealed class OffsetConverterTests
{
    // Named on a member, it writes that member in UTC whatever the serializer's other settings.
    [Fact]
    public void OnAMember_WritesUtcWithZ()
    {
        var written = JsonSerializer.Serialize(new Stamped(new DateTimeOffset(2026, 10, 11, 18, 0, 0, TimeSpan.FromHours(2))));

        Assert.Equal("""{"At":"2026-10-11T16:00:00Z"}""", written);
    }

    // A date keying a map is written, and read, as the same text.
    [Fact]
    public void AsAMapsKey_WrittenUtcWithZ_AndReadAsTheSameInstant()
    {
        var options = new JsonSerializerOptions { Converters = { new UtcDates.OffsetConverter() } };
        var at = new DateTimeOffset(2026, 10, 11, 18, 0, 0, TimeSpan.FromHours(2));

        var written = JsonSerializer.Serialize(new Dictionary<DateTimeOffset, int> { [at] = 1 }, options);
        var read = JsonSerializer.Deserialize<Dictionary<DateTimeOffset, int>>("""{"2026-10-11T18:00:00+02:00":1}""", options)!;

        Assert.Equal("""{"2026-10-11T16:00:00Z":1}""", written);
        Assert.Equal(at, Assert.Single(read.Keys));
    }

    [Fact]
    public void Write_NoWriter_Throws()
    {
        var converter = new UtcDates.OffsetConverter();

        Assert.Throws<ArgumentNullException>(() => converter.Write(null!, DateTimeOffset.UnixEpoch, JsonSerializerOptions.Default));
        Assert.Throws<ArgumentNullException>(() => converter.WriteAsPropertyName(null!, DateTimeOffset.UnixEpoch, JsonSerializerOptions.Default));
    }

    private sealed record Stamped([property: JsonConverter(typeof(UtcDates.OffsetConverter))] DateTimeOffset At);
}
