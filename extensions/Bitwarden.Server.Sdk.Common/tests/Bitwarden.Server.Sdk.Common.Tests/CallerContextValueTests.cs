namespace Bitwarden.Server.Sdk.Common.Tests;

public sealed class CallerContextValueTests
{
    public static TheoryData<CallerContextValue> NotEmpty =>
    [
        new CallerContextValue { ClientIp = "203.0.113.9" },
        new CallerContextValue { DeviceType = "10" },
        new CallerContextValue { DeviceIdentifier = "device-1" },
        new CallerContextValue { Credential = CallerCredential.User },
        new CallerContextValue { RecordedUser = Guid.NewGuid() },
        new CallerContextValue { RecordedProvider = Guid.NewGuid() },
        new CallerContextValue { Organizations = [Guid.NewGuid()] },
    ];

    [Fact]
    public void IsEmpty_WithNoMembers_IsTrue()
    {
        Assert.True(new CallerContextValue().IsEmpty);
    }

    [Fact]
    public void IsEmpty_WithAnEmptyOrganizationList_IsTrue()
    {
        Assert.True(new CallerContextValue { Organizations = [] }.IsEmpty);
    }

    [Theory]
    [MemberData(nameof(NotEmpty))]
    public void IsEmpty_WithAnyMember_IsFalse(CallerContextValue value)
    {
        Assert.False(value.IsEmpty);
    }
}
