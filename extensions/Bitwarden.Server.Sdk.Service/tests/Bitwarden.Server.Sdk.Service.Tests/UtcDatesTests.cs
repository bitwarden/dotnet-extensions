using System.Text.Json;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using Microsoft.AspNetCore.Builder;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// Every date a service writes is ISO 8601 UTC with a literal <c>Z</c>, and the published document
/// still types it as a <c>date-time</c> string, nullable where it is.
/// </summary>
public sealed class UtcDatesTests
{
    private static readonly Guid Person = new("a1000000-0000-4000-8000-00000000000d");

    private static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        UtcDates.Apply(options);
        return options;
    }

    [Theory]
    [InlineData("2026-10-11T16:07:53.8797511+00:00", "2026-10-11T16:07:53.8797511Z")]
    [InlineData("2026-10-11T18:07:53.8797511+02:00", "2026-10-11T16:07:53.8797511Z")]
    [InlineData("2026-10-11T16:07:53.8800000+00:00", "2026-10-11T16:07:53.88Z")]
    [InlineData("2026-10-11T16:07:53+00:00", "2026-10-11T16:07:53Z")]
    [InlineData("2026-10-11T00:30:00-05:00", "2026-10-11T05:30:00Z")]
    public void Write_Offset_UtcWithZ(string value, string expected)
    {
        var actual = JsonSerializer.Serialize(DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture), Options);

        Assert.Equal($"\"{expected}\"", actual);
    }

    [Fact]
    public void Write_DateTimeOfEachKind_UtcWithZ()
    {
        var utc = new DateTime(2026, 10, 11, 16, 7, 53, DateTimeKind.Utc).AddTicks(8_797_511);

        Assert.Equal("\"2026-10-11T16:07:53.8797511Z\"", JsonSerializer.Serialize(utc, Options));
        Assert.Equal("\"2026-10-11T16:07:53.8797511Z\"", JsonSerializer.Serialize(utc.ToLocalTime(), Options));
        Assert.Equal("\"2026-10-11T16:07:53.8797511Z\"", JsonSerializer.Serialize(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified), Options));
    }

    [Fact]
    public void Write_NullableAndInsideAnObject_UtcWithZ()
    {
        var actual = JsonSerializer.Serialize(
            new Dated(new DateTimeOffset(2026, 10, 11, 18, 0, 0, TimeSpan.FromHours(2)), null),
            Options);

        Assert.Equal("{\"at\":\"2026-10-11T16:00:00Z\",\"maybe\":null}", actual);
    }

    [Theory]
    [InlineData("\"2026-10-11T16:07:53.8797511Z\"")]
    [InlineData("\"2026-10-11T18:07:53.8797511+02:00\"")]
    [InlineData("\"2026-10-11T16:07:53.8797511+00:00\"")]
    public void Read_ZOrAnyOffset_TheSameInstant(string json)
    {
        var expected = new DateTimeOffset(2026, 10, 11, 16, 7, 53, TimeSpan.Zero).AddTicks(8_797_511);

        Assert.Equal(expected, JsonSerializer.Deserialize<DateTimeOffset>(json, Options));
        Assert.Equal(expected.UtcDateTime, JsonSerializer.Deserialize<DateTime>(json, Options));
        Assert.Equal(DateTimeKind.Utc, JsonSerializer.Deserialize<DateTime>(json, Options).Kind);
    }

    [Fact]
    public void Read_DateTimeWithoutAZone_TakenAsUtc()
    {
        var actual = JsonSerializer.Deserialize<DateTime>("\"2026-10-11T16:07:53\"", Options);

        Assert.Equal(DateTimeKind.Utc, actual.Kind);
        Assert.Equal(new DateTime(2026, 10, 11, 16, 7, 53, DateTimeKind.Utc), actual);
    }

    [Fact]
    public async Task Get_DateWithAnOffset_AnsweredInUtcWithZ()
    {
        await using var service = await SessionHost.StartAsync(productService: true);
        using var request = ProductServiceAttributeTests.Get("/api/v1/dated-probes/current", SessionHost.Token(Person, SessionHost.Scope));

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode, body);
        var attributes = JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("attributes");
        Assert.Equal("2026-10-11T12:00:00.1234567Z", attributes.GetProperty("at").GetString());
        Assert.Equal("2026-10-11T12:00:00Z", attributes.GetProperty("whole").GetString());
    }

    [Fact]
    public async Task OpenApi_Dates_StringsInDateTimeFormat_NullableWhereTheyAre()
    {
        await using var service = await SessionHost.StartAsync(productService: true);

        using var response = await service.Client().GetAsync("/api/openapi.json", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var properties = document.RootElement.GetProperty("components").GetProperty("schemas")
            .EnumerateObject()
            .Select(schema => schema.Value)
            .Where(schema => schema.TryGetProperty("properties", out var p) && p.TryGetProperty("whole", out _) && p.TryGetProperty("maybe", out _))
            .Select(schema => schema.GetProperty("properties"))
            .First();
        foreach (var name in new[] { "at", "whole", "maybe" })
        {
            var property = properties.GetProperty(name);
            Assert.Equal("string", property.GetProperty("type").GetString());
            Assert.Equal("date-time", property.GetProperty("format").GetString());
        }

        Assert.True(properties.GetProperty("maybe").GetProperty("nullable").GetBoolean());
        Assert.False(properties.GetProperty("whole").TryGetProperty("nullable", out _));
    }

    private sealed record Dated(DateTimeOffset At, DateTimeOffset? Maybe);
}

/// <summary>
/// A resource with dates, for <see cref="UtcDatesTests"/>.
/// </summary>
public sealed record DatedProbe : Resource
{
    /// <summary>
    /// An instant with a fraction, given at +02:00.
    /// </summary>
    [ReadOnly]
    public DateTimeOffset At { get; init; }

    /// <summary>
    /// A whole second.
    /// </summary>
    [ReadOnly]
    public DateTimeOffset Whole { get; init; }

    /// <summary>
    /// A date that may be absent.
    /// </summary>
    [ReadOnly]
    public DateTimeOffset? Maybe { get; init; }
}

/// <summary>
/// Answers one <see cref="DatedProbe"/>.
/// </summary>
public static class DatedProbesEndpoints
{
    /// <summary>
    /// Maps the probe.
    /// </summary>
    /// <param name="endpoints">The service's endpoints.</param>
    public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("api/v1/dated-probes").WithStandards().MapGet("current", GetCurrent);

    /// <summary>
    /// The probe
    /// </summary>
    /// <returns>The probe</returns>
    [StandardReadOne("current")]
    public static ResourceDocument<DatedProbe> GetCurrent() =>
        StandardResponses.ReadOneResponse(new DatedProbe
        {
            Id = "current",
            At = new DateTimeOffset(2026, 10, 11, 14, 0, 0, TimeSpan.FromHours(2)).AddTicks(1_234_567),
            Whole = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero),
        });
}
