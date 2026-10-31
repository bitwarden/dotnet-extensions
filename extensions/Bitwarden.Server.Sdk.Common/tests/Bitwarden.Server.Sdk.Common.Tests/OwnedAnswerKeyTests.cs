using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Bitwarden.Server.Sdk.Common.Tests;

public sealed class OwnedAnswerKeyTests
{
    private static readonly byte[] Secret = Enumerable.Repeat((byte)1, 32).ToArray();

    [Fact]
    public void Of_IsTheFirstSixteenBytesOfTheDigest_InOrder()
    {
        var subject = OwnedAnswerKey.Of("account-by-email", "user@example.com");

        Assert.Equal(Guid.Parse("2287adc1-b81a-8a83-04c7-0bfd109b5d4a"), subject);
        Assert.Equal("2287adc1b81a8a8304c70bfd109b5d4a", subject.ToString("N"));
    }

    [Fact]
    public void Of_WithASecret_IsTheFirstSixteenBytesOfTheKeyedDigest()
    {
        var subject = OwnedAnswerKey.Of("account-by-email", "user@example.com", Secret);

        Assert.Equal("f5c35293c26a2cc91ee90d972a30b995", subject.ToString("N"));
    }

    [Fact]
    public void Of_WithAnEmptySecret_IsThePlainDigest()
    {
        Assert.Equal(OwnedAnswerKey.Of("account-by-email", "user@example.com"), OwnedAnswerKey.Of("account-by-email", "user@example.com", []));
    }

    [Fact]
    public void Of_WithAnotherSecret_IsAnotherSubject()
    {
        var other = Enumerable.Repeat((byte)2, 32).ToArray();

        Assert.NotEqual(OwnedAnswerKey.Of("account-by-email", "user@example.com", Secret), OwnedAnswerKey.Of("account-by-email", "user@example.com", other));
    }

    [Fact]
    public void ReadSecret_Unset_IsEmpty()
    {
        Assert.Empty(OwnedAnswerKey.ReadSecret(Configuration(null)));
    }

    [Fact]
    public void ReadSecret_ReadsBase64()
    {
        Assert.Equal(Secret, OwnedAnswerKey.ReadSecret(Configuration(Convert.ToBase64String(Secret))));
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    public void ReadSecret_NotAUsableSecret_Throws(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OwnedAnswerKey.ReadSecret(Configuration(value)));

        Assert.Contains(OwnedAnswerKey.SecretKey, exception.Message);
        Assert.DoesNotContain(value, exception.Message);
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

    [Theory]
    [InlineData("account\nby-email")]
    [InlineData("account:by-email")]
    public void Of_AnAnswerThatCouldBeConfused_Throws(string answer)
    {
        Assert.Throws<ArgumentException>(() => OwnedAnswerKey.Of(answer, "user@example.com"));
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
        Assert.Equal("Caching:Owned:AnswerSecret", OwnedAnswerKey.SecretKey);
        Assert.Equal(32, OwnedAnswerKey.MinimumSecretLength);
    }

    private static IConfiguration Configuration(string? secret) => new SecretOnly(secret);

    private sealed class SecretOnly(string? secret) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == OwnedAnswerKey.SecretKey ? secret : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => throw new NotSupportedException();

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
