using System.Text.Json;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// The host's time zone, changed for one test at a time: nothing else in the assembly runs meanwhile.
/// </summary>
[CollectionDefinition(nameof(HostTimeZone), DisableParallelization = true)]
public sealed class HostTimeZone;

/// <summary>
/// A <see cref="DateTimeOffset"/> sent without a zone designator is read as UTC on any host, never as the
/// host's local time: a service keeps only UTC, and a client that leaves the zone out means UTC.
/// </summary>
[Collection(nameof(HostTimeZone))]
public sealed class ZonelessOffsetReadTests
{
    private sealed record Window(DateTimeOffset NotBefore, DateTimeOffset? NotAfter);

    [Theory]
    [InlineData("America/New_York")]
    [InlineData("Asia/Tokyo")]
    [InlineData("UTC")]
    public void Read_DateTimeOffsetWithoutAZone_TakenAsUtc(string zone)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        UtcDates.Apply(options);
        var expected = new DateTimeOffset(2026, 10, 11, 16, 7, 53, TimeSpan.Zero);

        Window read;
        using (InZone(zone))
        {
            read = JsonSerializer.Deserialize<Window>("""{"notBefore":"2026-10-11T16:07:53","notAfter":"2026-10-11T16:07:53"}""", options)!;
        }

        Assert.Equal(expected, read.NotBefore);
        Assert.Equal(TimeSpan.Zero, read.NotBefore.Offset);
        Assert.Equal(expected, read.NotAfter);
    }

    [Fact]
    public void Read_DateTimeOffsetWithAZone_KeepsItsInstant()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        UtcDates.Apply(options);

        Window read;
        using (InZone("America/New_York"))
        {
            read = JsonSerializer.Deserialize<Window>("""{"notBefore":"2026-10-11T18:07:53+02:00","notAfter":"2026-10-11T16:07:53Z"}""", options)!;
        }

        Assert.Equal(new DateTimeOffset(2026, 10, 11, 16, 7, 53, TimeSpan.Zero), read.NotBefore);
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 16, 7, 53, TimeSpan.Zero), read.NotAfter);
    }

    /// <summary>
    /// The host's zone, as <c>TZ</c> names it, until disposed.
    /// </summary>
    private static Restore InZone(string zone)
    {
        var before = System.Environment.GetEnvironmentVariable("TZ");
        System.Environment.SetEnvironmentVariable("TZ", zone);
        TimeZoneInfo.ClearCachedData();

        return new Restore(before);
    }

    private sealed class Restore(string? before) : IDisposable
    {
        public void Dispose()
        {
            System.Environment.SetEnvironmentVariable("TZ", before);
            TimeZoneInfo.ClearCachedData();
        }
    }
}
