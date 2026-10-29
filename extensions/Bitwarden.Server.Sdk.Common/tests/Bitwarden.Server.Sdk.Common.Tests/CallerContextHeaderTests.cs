using System.Text.Json;

namespace Bitwarden.Server.Sdk.Common.Tests;

/// <summary>
/// <c>Bitwarden-Context</c>: a closed, versioned object, written by the platform alone and read
/// strictly.
/// </summary>
public sealed class CallerContextHeaderTests
{
    private static readonly Guid Person = new("b0000000-0000-4000-8000-000000000001");

    [Fact]
    public void Write_EveryMember_IsVersionOneInOrder()
    {
        var written = CallerContextHeader.Write(new CallerContextValue
        {
            ClientIp = "203.0.113.9",
            DeviceType = "10",
            DeviceIdentifier = "device-1",
            Credential = CallerCredential.System,
            RecordedUser = Person,
        });

        Assert.Equal(
            $$"""{"v":1,"clientIp":"203.0.113.9","deviceType":"10","deviceIdentifier":"device-1","credential":"SYSTEM","recordedUser":"{{Person}}"}""",
            written);
    }

    // The provider a staff member acts through in a client: carried on any call, for the record alone.
    [Fact]
    public void Write_ThenParse_ARecordedProvider_RoundTrips()
    {
        var provider = new Guid("d0000000-0000-4000-8000-000000000001");

        var written = CallerContextHeader.Write(new CallerContextValue { Credential = CallerCredential.User, RecordedProvider = provider });

        Assert.Equal($$"""{"v":1,"credential":"USER","recordedProvider":"{{provider}}"}""", written);
        Assert.True(CallerContextHeader.TryParse(written, out var read, out _));
        Assert.Equal(provider, read.RecordedProvider);
    }

    // The acting person's organizations, for feature flags below the product layer.
    [Fact]
    public void Write_ThenParse_Organizations_RoundTrip()
    {
        var first = new Guid("a0000000-0000-4000-8000-000000000001");
        var second = new Guid("a0000000-0000-4000-8000-000000000002");

        var written = CallerContextHeader.Write(new CallerContextValue
        {
            Credential = CallerCredential.User,
            Organizations = [first, second],
        });

        Assert.Equal($$"""{"v":1,"credential":"USER","organizations":["{{first}}","{{second}}"]}""", written);
        Assert.True(CallerContextHeader.TryParse(written, out var read, out _));
        Assert.Equal([first, second], read.Organizations);
        Assert.False(read.IsEmpty);
    }

    [Fact]
    public void Write_NoOrganizations_LeavesTheMemberOut()
    {
        Assert.Equal("""{"v":1}""", CallerContextHeader.Write(new CallerContextValue { Organizations = [] }));
    }

    // The most a person may list fits the limit beside every other member.
    [Fact]
    public void Write_TheMostOrganizations_FitsTheLimit()
    {
        var written = CallerContextHeader.Write(new CallerContextValue
        {
            ClientIp = "2001:db8:ffff:ffff:ffff:ffff:ffff:ffff",
            DeviceType = new string('t', 50),
            DeviceIdentifier = new string('d', 50),
            Credential = CallerCredential.System,
            RecordedUser = Person,
            RecordedProvider = Person,
            Organizations = [.. Enumerable.Range(0, CallerContextHeader.MaximumOrganizations).Select(_ => Guid.NewGuid())],
        });

        Assert.True(CallerContextHeader.TryParse(written, out var read, out var problem), problem);
        Assert.Equal(CallerContextHeader.MaximumOrganizations, read.Organizations!.Count);
    }

    [Fact]
    public void Write_MoreOrganizationsThanTheMost_Throws()
    {
        Assert.Throws<ArgumentException>(() => CallerContextHeader.Write(new CallerContextValue
        {
            Organizations = [.. Enumerable.Range(0, CallerContextHeader.MaximumOrganizations + 1).Select(_ => Guid.NewGuid())],
        }));
    }

    // Never absent: it always says which version it speaks.
    [Fact]
    public void Write_NothingToCarry_IsTheVersionAlone()
    {
        Assert.Equal("""{"v":1}""", CallerContextHeader.Write(new CallerContextValue()));
    }

    [Fact]
    public void Write_ARecordedPersonWithoutSystem_Throws()
    {
        Assert.Throws<ArgumentException>(() => CallerContextHeader.Write(new CallerContextValue
        {
            Credential = CallerCredential.User,
            RecordedUser = Person,
        }));
    }

    [Theory]
    [InlineData(CallerCredential.User, "USER")]
    [InlineData(CallerCredential.Organization, "ORGANIZATION")]
    [InlineData(CallerCredential.Machine, "MACHINE")]
    [InlineData(CallerCredential.System, "SYSTEM")]
    public void RoundTrip_EveryCredential_IsAllCaps(CallerCredential credential, string name)
    {
        var written = CallerContextHeader.Write(new CallerContextValue { Credential = credential })!;

        Assert.Contains($"\"credential\":\"{name}\"", written, StringComparison.Ordinal);
        Assert.True(CallerContextHeader.TryParse(written, out var read, out _));
        Assert.Equal(credential, read.Credential);
    }

    [Fact]
    public void TryParse_AnAddress_IsCanonical()
    {
        Assert.True(CallerContextHeader.TryParse("""{"v":1,"clientIp":"2001:0db8:0000:0000:0000:0000:0000:0001"}""", out var read, out _));

        Assert.Equal("2001:db8::1", read.ClientIp);
    }

    [Theory]
    // Not an object, or not one.
    [InlineData("")]
    [InlineData("1")]
    [InlineData("[]")]
    [InlineData("\"v\"")]
    [InlineData("""{"v":1}{"v":1}""")]
    [InlineData("""{"v":1""")]
    // The version: required, a number, and 1.
    [InlineData("""{}""")]
    [InlineData("""{"v":"1"}""")]
    [InlineData("""{"v":2}""")]
    [InlineData("""{"v":1.5}""")]
    // A closed object: nothing unknown, nothing twice.
    [InlineData("""{"v":1,"organization":"a0000000-0000-4000-8000-000000000001"}""")]
    [InlineData("""{"v":1,"clientIp":"203.0.113.9","clientIp":"198.51.100.1"}""")]
    [InlineData("""{"v":1,"v":1}""")]
    [InlineData("""{"v":1,"clientip":"203.0.113.9"}""")]
    // Each member's kind and shape.
    [InlineData("""{"v":1,"clientIp":null}""")]
    [InlineData("""{"v":1,"clientIp":12}""")]
    [InlineData("""{"v":1,"clientIp":"not an address"}""")]
    [InlineData("""{"v":1,"deviceType":"a device; with prose"}""")]
    [InlineData("""{"v":1,"deviceIdentifier":"has spaces in it"}""")]
    [InlineData("""{"v":1,"deviceIdentifier":{"nested":1}}""")]
    [InlineData("""{"v":1,"credential":"user"}""")]
    [InlineData("""{"v":1,"credential":"ADMIN"}""")]
    // recordedUser: an identifier, on a SYSTEM call alone.
    [InlineData("""{"v":1,"recordedUser":"b0000000-0000-4000-8000-000000000001"}""")]
    [InlineData("""{"v":1,"credential":"USER","recordedUser":"b0000000-0000-4000-8000-000000000001"}""")]
    [InlineData("""{"v":1,"credential":"SYSTEM","recordedUser":"not-an-identifier"}""")]
    [InlineData("""{"v":1,"credential":"SYSTEM","recordedUser":"00000000-0000-0000-0000-000000000000"}""")]
    // recordedProvider: an identifier.
    [InlineData("""{"v":1,"recordedProvider":"not-an-identifier"}""")]
    [InlineData("""{"v":1,"recordedProvider":"00000000-0000-0000-0000-000000000000"}""")]
    // organizations: an array of distinct identifiers.
    [InlineData("""{"v":1,"organizations":"a0000000-0000-4000-8000-000000000001"}""")]
    [InlineData("""{"v":1,"organizations":["not-an-identifier"]}""")]
    [InlineData("""{"v":1,"organizations":[1]}""")]
    [InlineData("""{"v":1,"organizations":[null]}""")]
    [InlineData("""{"v":1,"organizations":[["a0000000-0000-4000-8000-000000000001"]]}""")]
    [InlineData("""{"v":1,"organizations":[{"id":"a0000000-0000-4000-8000-000000000001"}]}""")]
    [InlineData("""{"v":1,"organizations":["00000000-0000-0000-0000-000000000000"]}""")]
    [InlineData("""{"v":1,"organizations":["a0000000-0000-4000-8000-000000000001","a0000000-0000-4000-8000-000000000001"]}""")]
    [InlineData("""{"v":1,"organizations":[""")]
    [InlineData("{\"v\":1,\"clientIp\"")]
    [InlineData("{\"v\":1,\"organizations\":[\"a0000000-0000-4000-8000-000000000001\"")]
    public void TryParse_Malformed_IsRefused(string header)
    {
        Assert.False(CallerContextHeader.TryParse(header, out _, out var problem));
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    public static TheoryData<CallerContextValue> Unsendable =>
    [
        new CallerContextValue { ClientIp = "not an address" },
        new CallerContextValue { DeviceType = "a device; with prose" },
        new CallerContextValue { DeviceIdentifier = "has spaces in it" },
        new CallerContextValue { Credential = (CallerCredential)99 },
        new CallerContextValue { RecordedProvider = Guid.Empty },
    ];

    [Theory]
    [MemberData(nameof(Unsendable))]
    public void Write_AMemberThatCannotBeSent_Throws(CallerContextValue value)
    {
        Assert.Throws<ArgumentException>(() => CallerContextHeader.Write(value));
    }

    [Fact]
    public void Name_AnUndefinedCredential_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CallerContextHeader.Name((CallerCredential)99));
    }

    [Fact]
    public void TryParse_LongerThanTheLimit_IsRefused()
    {
        var header = $$"""{"v":1,"deviceType":"10"{{new string(' ', CallerContextHeader.MaximumLength)}}}""";

        Assert.False(CallerContextHeader.TryParse(header, out _, out var problem));
        Assert.Contains("16384", problem, StringComparison.Ordinal);
    }

    // A refusal names the member, and never echoes what the caller sent.
    [Fact]
    public void TryParse_AnUnknownMember_NamesItWithoutItsValue()
    {
        Assert.False(CallerContextHeader.TryParse("""{"v":1,"secret":"do-not-echo"}""", out _, out var problem));

        Assert.Contains("secret", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-echo", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Credential_InAnEnvelope_IsWrittenAllCaps()
    {
        var json = JsonSerializer.Serialize(new { Credential = (CallerCredential?)CallerCredential.Machine });

        Assert.Equal("""{"Credential":"MACHINE"}""", json);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CallerCredential>("\"machine\""));
    }
}
