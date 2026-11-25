namespace Bitwarden.Server.Sdk.Context.Tests;

public sealed class ContextHeaderTests
{
    private static readonly Guid Person = new("b0000000-0000-4000-8000-000000000001");

    [Fact]
    public void Write_EveryMember_IsVersionOneInOrder()
    {
        var written = ContextHeader.Write(new ContextValue
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

    [Fact]
    public void Write_ThenParse_ARecordedProvider_RoundTrips()
    {
        var provider = new Guid("d0000000-0000-4000-8000-000000000001");

        var written = ContextHeader.Write(new ContextValue { Credential = CallerCredential.User, RecordedProvider = provider });

        Assert.Equal($$"""{"v":1,"credential":"USER","recordedProvider":"{{provider}}"}""", written);
        Assert.True(ContextHeader.TryParse(written, out var read, out _));
        Assert.Equal(provider, read.RecordedProvider);
    }

    [Fact]
    public void Write_ThenParse_Organizations_RoundTrip()
    {
        var first = new Guid("a0000000-0000-4000-8000-000000000001");
        var second = new Guid("a0000000-0000-4000-8000-000000000002");

        var written = ContextHeader.Write(new ContextValue
        {
            Credential = CallerCredential.User,
            Organizations = [first, second],
        });

        Assert.Equal($$"""{"v":1,"credential":"USER","organizations":["{{first}}","{{second}}"]}""", written);
        Assert.True(ContextHeader.TryParse(written, out var read, out _));
        Assert.Equal([first, second], read.Organizations);
        Assert.False(read.IsEmpty);
    }

    [Fact]
    public void Write_NoOrganizations_LeavesTheMemberOut()
    {
        Assert.Equal("""{"v":1}""", ContextHeader.Write(new ContextValue { Organizations = [] }));
    }

    [Fact]
    public void Write_TheMostOrganizations_FitsTheLimit()
    {
        var written = ContextHeader.Write(new ContextValue
        {
            ClientIp = "2001:db8:ffff:ffff:ffff:ffff:ffff:ffff",
            DeviceType = new string('t', 50),
            DeviceIdentifier = new string('d', 50),
            Credential = CallerCredential.System,
            RecordedUser = Person,
            RecordedProvider = Person,
            Organizations = [.. Enumerable.Range(0, ContextHeader.MaximumOrganizations).Select(_ => Guid.NewGuid())],
        });

        Assert.True(ContextHeader.TryParse(written, out var read, out var problem), problem);
        Assert.Equal(ContextHeader.MaximumOrganizations, read.Organizations!.Count);
    }

    [Fact]
    public void Write_MoreOrganizationsThanTheMost_Throws()
    {
        Assert.Throws<ArgumentException>(() => ContextHeader.Write(new ContextValue
        {
            Organizations = [.. Enumerable.Range(0, ContextHeader.MaximumOrganizations + 1).Select(_ => Guid.NewGuid())],
        }));
    }

    // The party flags are evaluated for, and the client's version, after the members before them.
    [Theory]
    [InlineData(FlagSubjectKind.Organization, "ORGANIZATION")]
    [InlineData(FlagSubjectKind.ServiceAccount, "SERVICE_ACCOUNT")]
    [InlineData(FlagSubjectKind.AccessConnector, "ACCESS_CONNECTOR")]
    public void Write_ThenParse_TheFlagSubjectAndVersion_RoundTrip(FlagSubjectKind kind, string name)
    {
        var key = new Guid("c0000000-0000-4000-8000-000000000001");
        var organization = new Guid("a0000000-0000-4000-8000-000000000001");

        var written = ContextHeader.Write(new ContextValue
        {
            Credential = CallerCredential.Organization,
            Organizations = [organization],
            FlagKind = kind,
            FlagKey = key,
            ClientVersion = "2026.10.1",
            ClientVersionIsPrerelease = true,
        });

        Assert.Equal(
            $$"""{"v":1,"credential":"ORGANIZATION","organizations":["{{organization}}"],"flagKind":"{{name}}","flagKey":"{{key}}","clientVersion":"2026.10.1","clientVersionIsPrerelease":true}""",
            written);
        Assert.True(ContextHeader.TryParse(written, out var read, out var problem), problem);
        Assert.Equal(kind, read.FlagKind);
        Assert.Equal(key, read.FlagKey);
        Assert.Equal("2026.10.1", read.ClientVersion);
        Assert.True(read.ClientVersionIsPrerelease);
        Assert.Equal(written, ContextHeader.Write(read));
    }

    // A user is the default kind, so it is left out, and read back the same whether written or not.
    [Fact]
    public void Write_AUsersFlagSubject_LeavesTheKindOut()
    {
        var written = ContextHeader.Write(new ContextValue { FlagKind = FlagSubjectKind.User, FlagKey = Person, ClientVersion = "1.2" });

        Assert.Equal($$"""{"v":1,"flagKey":"{{Person}}","clientVersion":"1.2"}""", written);
        Assert.True(ContextHeader.TryParse($$"""{"v":1,"flagKind":"USER","flagKey":"{{Person}}"}""", out var read, out _));
        Assert.Equal(FlagSubjectKind.User, read.FlagKind);
        Assert.Equal(Person, read.FlagKey);
    }

    // A version that isn't a prerelease says nothing about it.
    [Fact]
    public void Write_NotAPrerelease_LeavesTheMarkerOut()
    {
        Assert.Equal(
            """{"v":1,"clientVersion":"2026.10.0.1"}""",
            ContextHeader.Write(new ContextValue { ClientVersion = "2026.10.0.1" }));
    }

    [Theory]
    [InlineData("1.2", true)]
    [InlineData("2026.10.0", true)]
    [InlineData("1.2.3.4", true)]
    [InlineData("1", false)]
    [InlineData("1.2.3.4.5", false)]
    [InlineData("1.2.3-beta", false)]
    [InlineData("1.2\n", false)]
    [InlineData("1234567890.1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsClientVersion_TwoToFourWholeNumbers(string? value, bool expected)
    {
        Assert.Equal(expected, ContextHeader.IsClientVersion(value));
    }

    [Fact]
    public void Write_NothingToCarry_IsTheVersionAlone()
    {
        Assert.Equal("""{"v":1}""", ContextHeader.Write(new ContextValue()));
    }

    [Fact]
    public void Write_ARecordedPersonWithoutSystem_Throws()
    {
        Assert.Throws<ArgumentException>(() => ContextHeader.Write(new ContextValue
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
        var written = ContextHeader.Write(new ContextValue { Credential = credential })!;

        Assert.Contains($"\"credential\":\"{name}\"", written, StringComparison.Ordinal);
        Assert.True(ContextHeader.TryParse(written, out var read, out _));
        Assert.Equal(credential, read.Credential);
    }

    [Theory]
    [InlineData("2001:0db8:0000:0000:0000:0000:0000:0001", "2001:db8::1")]
    [InlineData("2001:DB8::1", "2001:db8::1")]
    [InlineData("203.0.113.9", "203.0.113.9")]
    // A dual-stack socket reports an IPv4 client as IPv6: one client, one spelling.
    [InlineData("::ffff:203.0.113.9", "203.0.113.9")]
    public void Write_AnAddress_IsCanonical(string address, string canonical)
    {
        Assert.Equal($$"""{"v":1,"clientIp":"{{canonical}}"}""", ContextHeader.Write(new ContextValue { ClientIp = address }));
    }

    [Theory]
    [InlineData("2001:0db8:0000:0000:0000:0000:0000:0001", "2001:db8::1")]
    [InlineData("::ffff:203.0.113.9", "203.0.113.9")]
    public void TryParse_AnAddress_IsCanonical(string address, string canonical)
    {
        Assert.True(ContextHeader.TryParse($$"""{"v":1,"clientIp":"{{address}}"}""", out var read, out _));

        Assert.Equal(canonical, read.ClientIp);
    }

    [Fact]
    public void TryParse_AnotherVersion_IsRefusedWithoutEchoingIt()
    {
        Assert.False(ContextHeader.TryParse("""{"v":987654}""", out _, out var problem));

        Assert.Contains("'v'", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("987654", problem, StringComparison.Ordinal);
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
    [InlineData("""{"v":1,"clientIp":"fe80::1%2"}""")]
    [InlineData("""{"v":1,"clientIp":"fe80::1%eth0"}""")]
    [InlineData("""{"v":1,"clientIp":"[2001:db8::1]"}""")]
    [InlineData("""{"v":1,"clientIp":"1"}""")]
    [InlineData("""{"v":1,"clientIp":"4294967295"}""")]
    [InlineData("""{"v":1,"clientIp":"127.1"}""")]
    [InlineData("""{"v":1,"clientIp":"017.0.0.1"}""")]
    [InlineData("""{"v":1,"clientIp":"0x7f.0.0.1"}""")]
    // The unspecified address names no client.
    [InlineData("""{"v":1,"clientIp":"0.0.0.0"}""")]
    [InlineData("""{"v":1,"clientIp":"::"}""")]
    [InlineData("""{"v":1,"clientIp":"0:0:0:0:0:0:0:0"}""")]
    [InlineData("""{"v":1,"clientIp":"::ffff:0.0.0.0"}""")]
    [InlineData("""{"v":1,"deviceType":"a device; with prose"}""")]
    [InlineData("""{"v":1,"deviceType":"10\n"}""")]
    [InlineData("""{"v":1,"deviceIdentifier":"device-1\n"}""")]
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
    [InlineData("""{"v":1,"flagKind":"PERSON"}""")]
    [InlineData("""{"v":1,"flagKind":"user"}""")]
    [InlineData("""{"v":1,"flagKind":1}""")]
    [InlineData("""{"v":1,"flagKey":"not-an-identifier"}""")]
    [InlineData("""{"v":1,"flagKey":"00000000-0000-0000-0000-000000000000"}""")]
    [InlineData("""{"v":1,"flagKey":"{c0000000-0000-4000-8000-000000000001}"}""")]
    // An identifier padded with whitespace, which Guid.TryParseExact would take.
    [InlineData("""{"v":1,"flagKey":" c0000000-0000-4000-8000-000000000001"}""")]
    [InlineData("""{"v":1,"flagKey":"c0000000-0000-4000-8000-000000000001 "}""")]
    [InlineData("""{"v":1,"credential":"SYSTEM","recordedUser":" b0000000-0000-4000-8000-000000000001 "}""")]
    [InlineData("""{"v":1,"recordedProvider":"\td0000000-0000-4000-8000-000000000001"}""")]
    [InlineData("""{"v":1,"organizations":["a0000000-0000-4000-8000-000000000001\n"]}""")]
    [InlineData("""{"v":1,"clientVersion":"2026"}""")]
    [InlineData("""{"v":1,"clientVersion":"2026.10.0-beta"}""")]
    [InlineData("""{"v":1,"clientVersion":2026.1}""")]
    [InlineData("""{"v":1,"clientVersionIsPrerelease":true}""")]
    [InlineData("""{"v":1,"clientVersion":"2026.10.0","clientVersionIsPrerelease":false}""")]
    [InlineData("""{"v":1,"clientVersion":"2026.10.0","clientVersionIsPrerelease":"true"}""")]
    [InlineData("""{"v":1,"organizations":[""")]
    [InlineData("{\"v\":1,\"clientIp\"")]
    [InlineData("{\"v\":1,\"organizations\":[\"a0000000-0000-4000-8000-000000000001\"")]
    // A lone surrogate in any string.
    [InlineData("""{"v":1,"\uD800":1}""")]
    [InlineData("""{"v":1,"deviceType":"\uD800"}""")]
    [InlineData("""{"v":1,"organizations":["\uD800"]}""")]
    public void TryParse_Malformed_IsRefused(string header)
    {
        Assert.False(ContextHeader.TryParse(header, out _, out var problem));
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    public static TheoryData<ContextValue> Unsendable =>
    [
        new ContextValue { ClientIp = "not an address" },
        new ContextValue { ClientIp = "fe80::1%2" },
        new ContextValue { ClientIp = "fe80::1%eth0" },
        new ContextValue { ClientIp = "[2001:db8::1]" },
        new ContextValue { ClientIp = "1" },
        new ContextValue { ClientIp = "4294967295" },
        new ContextValue { ClientIp = "127.1" },
        new ContextValue { ClientIp = "017.0.0.1" },
        new ContextValue { ClientIp = "0x7f.0.0.1" },
        new ContextValue { ClientIp = "0.0.0.0" },
        new ContextValue { ClientIp = "::" },
        new ContextValue { DeviceType = "a device; with prose" },
        new ContextValue { DeviceType = "10\n" },
        new ContextValue { DeviceIdentifier = "device-1\n" },
        new ContextValue { DeviceIdentifier = "has spaces in it" },
        new ContextValue { Credential = (CallerCredential)99 },
        new ContextValue { RecordedProvider = Guid.Empty },
        new ContextValue { Credential = CallerCredential.System, RecordedUser = Guid.Empty },
        new ContextValue { Organizations = [Guid.Empty] },
        new ContextValue { FlagKind = (FlagSubjectKind)99 },
        new ContextValue { FlagKey = Guid.Empty },
        new ContextValue { ClientVersion = "2026.10.0-beta" },
        new ContextValue { ClientVersionIsPrerelease = true },
    ];

    [Theory]
    [MemberData(nameof(Unsendable))]
    public void Write_AMemberThatCannotBeSent_Throws(ContextValue value)
    {
        Assert.Throws<ArgumentException>(() => ContextHeader.Write(value));
    }

    [Fact]
    public void Name_AnUndefinedCredential_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ContextHeader.Name((CallerCredential)99));
    }

    [Fact]
    public void TryParse_LongerThanTheLimit_IsRefused()
    {
        var header = $$"""{"v":1,"deviceType":"10"{{new string(' ', ContextHeader.MaximumLength)}}}""";

        Assert.False(ContextHeader.TryParse(header, out _, out var problem));
        Assert.Contains("16384", problem, StringComparison.Ordinal);
    }

    // Fewer characters than the limit, but more bytes once each two-byte character is counted.
    [Fact]
    public void TryParse_LongerThanTheLimitInBytesOnly_IsRefused()
    {
        var start = "{\"v\":1,\"deviceType\":\"10\",\"";
        var header = start + new string('\u00e9', ContextHeader.MaximumLength / 2) + "\":1}";

        Assert.True(header.Length <= ContextHeader.MaximumLength);
        Assert.False(ContextHeader.TryParse(header, out _, out var problem));
        Assert.Contains("16384", problem, StringComparison.Ordinal);
    }

    // A value far over the limit, as a transport with no header limit could hand over, is refused without a copy.
    [Fact]
    public void TryParse_FarLongerThanTheLimit_IsRefusedWithoutCopyingIt()
    {
        var header = new string(' ', 10_000_000);
        var before = GC.GetAllocatedBytesForCurrentThread();

        Assert.False(ContextHeader.TryParse(header, out _, out _));

        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 10_000);
    }

    [Fact]
    public void TryParse_ExactlyTheLimit_IsRead()
    {
        var start = "{\"v\":1,\"deviceType\":\"10\"";
        var header = start + new string(' ', ContextHeader.MaximumLength - start.Length - 1) + "}";

        Assert.Equal(ContextHeader.MaximumLength, System.Text.Encoding.UTF8.GetByteCount(header));
        Assert.True(ContextHeader.TryParse(header, out var read, out var problem), problem);
        Assert.Equal("10", read.DeviceType);
    }

    [Fact]
    public void TryParse_AnUnknownMember_NamesItWithoutItsValue()
    {
        Assert.False(ContextHeader.TryParse("""{"v":1,"secret":"do-not-echo"}""", out _, out var problem));

        Assert.Contains("secret", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-echo", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_AnUnknownMemberLongerThan32_IsNotNamed()
    {
        var name = new string('m', 33);

        Assert.False(ContextHeader.TryParse($$"""{"v":1,"{{name}}":1}""", out _, out var problem));

        Assert.Contains("a member", problem, StringComparison.Ordinal);
        Assert.DoesNotContain(name, problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("10", true)]
    [InlineData("10\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsDeviceType_AcceptsOnlyAShortName(string? value, bool expected)
    {
        Assert.Equal(expected, ContextHeader.IsDeviceType(value));
    }

    [Fact]
    public void IsDeviceType_AtMost50()
    {
        Assert.True(ContextHeader.IsDeviceType(new string('t', 50)));
        Assert.False(ContextHeader.IsDeviceType(new string('t', 51)));
    }

    [Fact]
    public void IsDeviceIdentifier_AtMost50()
    {
        Assert.True(ContextHeader.IsDeviceIdentifier(new string('d', 50)));
        Assert.False(ContextHeader.IsDeviceIdentifier(new string('d', 51)));
    }

    [Theory]
    [InlineData("device:1_a-b", true)]
    [InlineData("device-1\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsDeviceIdentifier_AcceptsOnlyWhatAPushTagAllows(string? value, bool expected)
    {
        Assert.Equal(expected, ContextHeader.IsDeviceIdentifier(value));
    }

    [Fact]
    public void TryParse_AnUnknownMemberEndingInALineFeed_IsNotNamed()
    {
        Assert.False(ContextHeader.TryParse("""{"v":1,"secret\n":1}""", out _, out var problem));

        Assert.Contains("a member", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", problem, StringComparison.Ordinal);
    }
}
