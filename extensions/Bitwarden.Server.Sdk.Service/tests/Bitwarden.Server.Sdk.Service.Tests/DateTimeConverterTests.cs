using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// <see cref="UtcDates.DateTimeConverter"/> on its own, named on a member: written as UTC with a <c>Z</c>, and read
/// back as a UTC date whatever zone it was sent in.
/// </summary>
public sealed class DateTimeConverterTests
{
    [Fact]
    public void OnAMember_WritesUtcWithZ()
    {
        var at = new DateTime(2026, 10, 11, 16, 0, 0, DateTimeKind.Utc);

        Assert.Equal("""{"At":"2026-10-11T16:00:00Z"}""", JsonSerializer.Serialize(new Stamped(at)));
        Assert.Equal("""{"At":"2026-10-11T16:00:00Z"}""", JsonSerializer.Serialize(new Stamped(DateTime.SpecifyKind(at, DateTimeKind.Unspecified))));
    }

    [Theory]
    [InlineData("""{"At":"2026-10-11T18:00:00+02:00"}""")]
    [InlineData("""{"At":"2026-10-11T16:00:00"}""")]
    public void OnAMember_ReadsAUtcDate(string json)
    {
        var read = JsonSerializer.Deserialize<Stamped>(json)!;

        Assert.Equal(DateTimeKind.Utc, read.At.Kind);
        Assert.Equal(new DateTime(2026, 10, 11, 16, 0, 0, DateTimeKind.Utc), read.At);
    }

    [Fact]
    public void Write_NoWriter_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new UtcDates.DateTimeConverter().Write(null!, DateTime.UnixEpoch, JsonSerializerOptions.Default));

    private sealed record Stamped([property: JsonConverter(typeof(UtcDates.DateTimeConverter))] DateTime At);
}
