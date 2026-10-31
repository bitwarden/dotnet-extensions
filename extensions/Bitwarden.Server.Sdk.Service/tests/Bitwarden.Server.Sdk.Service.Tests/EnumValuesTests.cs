
namespace Bitwarden.Server.Sdk.Service.Tests;

// A published name reads back to the member that wrote it, whatever the member is called. Some
// names carry underscores (User_LoggedIn), and stripping them to parse would fail with a 500.
public sealed class EnumValuesTests
{
    public enum Legacy
    {
        User_LoggedIn,
        PromotionalCredit,
    }

    [Theory]
    [InlineData("USER_LOGGED_IN", Legacy.User_LoggedIn)]
    [InlineData("user_logged_in", Legacy.User_LoggedIn)]
    [InlineData("PROMOTIONAL_CREDIT", Legacy.PromotionalCredit)]
    public void TryParse_PublishedName_IsTheMember(string value, Legacy expected)
    {
        Assert.True(EnumValues.TryParse(typeof(Legacy), value, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("UserLoggedIn")]
    [InlineData("USERLOGGEDIN")]
    [InlineData("0")]
    public void TryParse_AnythingElse_IsNotAMember(string value)
    {
        Assert.False(EnumValues.TryParse(typeof(Legacy), value, out _));
    }

    // A query's filter is checked against the published names, without case.
    [Theory]
    [InlineData("USER_LOGGED_IN", true)]
    [InlineData("promotional_credit", true)]
    [InlineData("User_LoggedIn", false)]
    [InlineData("", false)]
    public void Recognises_OnlyAPublishedName(string value, bool expected) =>
        Assert.Equal(expected, EnumValues.Recognises(typeof(Legacy), value));
}
