namespace Bitwarden.Server.Sdk.Common.Tests;

public sealed class OwnedAnswerKeyTests
{
    [Fact]
    public void Of_IsTheFirstSixteenBytesOfTheDigest()
    {
        Assert.Equal(Guid.Parse("c1ad8722-1ab8-838a-04c7-0bfd109b5d4a"), OwnedAnswerKey.Of("account-by-email", "user@example.com"));
    }

    [Fact]
    public void Of_TheSameKeyForTwoAnswers_IsTwoSubjects()
    {
        Assert.NotEqual(OwnedAnswerKey.Of("account-by-email", "user@example.com"), OwnedAnswerKey.Of("invitation-by-email", "user@example.com"));
    }

    [Fact]
    public void Of_DoesNotNormalizeTheKey()
    {
        Assert.NotEqual(OwnedAnswerKey.Of("account-by-email", "user@example.com"), OwnedAnswerKey.Of("account-by-email", "User@Example.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Of_WithoutAnAnswer_Throws(string? answer)
    {
        Assert.ThrowsAny<ArgumentException>(() => OwnedAnswerKey.Of(answer!, "user@example.com"));
    }

    [Fact]
    public void Of_WithoutAKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => OwnedAnswerKey.Of("account-by-email", null!));
    }

    [Fact]
    public void Template_IsUnderTheOwnersPrefix()
    {
        var template = OwnedAnswerKey.Template("widget-service", "widget-by-name");

        Assert.Equal("widget-service:answer:widget-by-name:{key}", template);
        Assert.StartsWith(OwnedCacheEntry.Prefix("widget-service"), template);
    }

    [Fact]
    public void Constants_AreTheContract()
    {
        Assert.Equal("x-bitwarden-answer", OwnedAnswerKey.Extension);
        Assert.Equal("sha-256", OwnedAnswerKey.Digest);
    }
}
