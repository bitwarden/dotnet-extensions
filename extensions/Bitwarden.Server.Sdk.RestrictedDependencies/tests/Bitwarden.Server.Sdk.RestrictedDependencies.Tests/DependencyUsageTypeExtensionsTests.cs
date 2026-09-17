namespace Bitwarden.Server.Sdk.RestrictedDependencies.Tests;

/// <summary>
/// <c>ToToken</c> and <c>TryParse</c> are two independent hand-written switches over the same
/// enum, and <c>ToToken</c>'s <c>_ =&gt; throw</c> arm suppresses the exhaustiveness warning that
/// would otherwise catch a new member. That throw would reach Roslyn as AD0001 rather than a
/// diagnostic, because every caller catches only <see cref="FormatException"/>.
/// </summary>
public class DependencyUsageTypeExtensionsTests
{
    [Fact]
    public void EveryKind_RoundTripsThroughItsToken()
    {
        Assert.All(Enum.GetValues<DependencyUsageType>(), kind =>
        {
            Assert.True(DependencyUsageTypeExtensions.TryParse(kind.ToToken(), out var parsed), $"'{kind}' has no token.");
            Assert.Equal(kind, parsed);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("bogus")]
    [InlineData("Member")]
    [InlineData("member ")]
    public void TryParse_UnknownToken_ReturnsFalseRatherThanThrowing(string token)
    {
        Assert.False(DependencyUsageTypeExtensions.TryParse(token, out _));
    }
}
