using Newtonsoft.Json;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// A generated client sends every date as ISO 8601 UTC with a literal <c>Z</c>.
/// </summary>
public sealed class UtcDateJsonConverterTests
{
    // As NSwag generates a client's settings when its options name the converter.
    private static readonly JsonSerializerSettings Settings = new() { Converters = [new UtcDateJsonConverter()] };

    [Fact]
    public void Serialize_OffsetsAndKinds_UtcWithZ()
    {
        var at = new DateTimeOffset(2026, 10, 11, 18, 7, 53, TimeSpan.FromHours(2)).AddTicks(8_797_511);

        var actual = JsonConvert.SerializeObject(
            new Body { At = at, Maybe = null, Whole = new DateTimeOffset(2026, 10, 11, 16, 0, 0, TimeSpan.Zero), Time = at.UtcDateTime.ToLocalTime() },
            Settings);

        Assert.Equal(
            "{\"At\":\"2026-10-11T16:07:53.8797511Z\",\"Maybe\":null,\"Whole\":\"2026-10-11T16:00:00Z\",\"Time\":\"2026-10-11T16:07:53.8797511Z\"}",
            actual);
    }

    [Theory]
    [InlineData("2026-10-11T16:07:53.8797511Z")]
    [InlineData("2026-10-11T18:07:53.8797511+02:00")]
    [InlineData("2026-10-11T16:07:53.8797511+00:00")]
    public void Deserialize_ZOrAnyOffset_TheSameInstant(string text)
    {
        var expected = new DateTimeOffset(2026, 10, 11, 16, 7, 53, TimeSpan.Zero).AddTicks(8_797_511);

        var actual = JsonConvert.DeserializeObject<Body>($"{{\"At\":\"{text}\",\"Maybe\":\"{text}\",\"Time\":\"{text}\"}}", Settings)!;

        Assert.Equal(expected, actual.At);
        Assert.Equal(expected, actual.Maybe);
        Assert.Equal(expected.UtcDateTime, actual.Time);
        Assert.Equal(DateTimeKind.Utc, actual.Time.Kind);
    }

    [Fact]
    public void Deserialize_Null_NullForAnOptionalDate()
    {
        Assert.Null(JsonConvert.DeserializeObject<Body>("{\"Maybe\":null}", Settings)!.Maybe);
    }

    private sealed class Body
    {
        public DateTimeOffset At { get; set; }

        public DateTimeOffset? Maybe { get; set; }

        public DateTimeOffset Whole { get; set; }

        public DateTime Time { get; set; }
    }
}
