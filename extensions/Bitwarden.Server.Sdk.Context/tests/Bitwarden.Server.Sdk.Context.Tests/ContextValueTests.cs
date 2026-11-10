namespace Bitwarden.Server.Sdk.Context.Tests;

public sealed class ContextValueTests
{
    public static TheoryData<ContextValue> NotEmpty =>
    [
        new ContextValue { ClientIp = "203.0.113.9" },
        new ContextValue { DeviceType = "10" },
        new ContextValue { DeviceIdentifier = "device-1" },
        new ContextValue { Credential = CallerCredential.User },
        new ContextValue { RecordedUser = Guid.NewGuid() },
        new ContextValue { RecordedProvider = Guid.NewGuid() },
        new ContextValue { Organizations = [Guid.NewGuid()] },
    ];

    [Fact]
    public void IsEmpty_WithNoMembers_IsTrue()
    {
        Assert.True(new ContextValue().IsEmpty);
    }

    [Fact]
    public void IsEmpty_WithAnEmptyOrganizationList_IsTrue()
    {
        Assert.True(new ContextValue { Organizations = [] }.IsEmpty);
    }

    [Theory]
    [MemberData(nameof(NotEmpty))]
    public void IsEmpty_WithAnyMember_IsFalse(ContextValue value)
    {
        Assert.False(value.IsEmpty);
    }
}
