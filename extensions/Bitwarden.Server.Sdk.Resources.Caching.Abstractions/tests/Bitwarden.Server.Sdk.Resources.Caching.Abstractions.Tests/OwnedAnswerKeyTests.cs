using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Bitwarden.Server.Sdk.Resources.Caching.Abstractions.Tests;

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

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void ReadSecret_Blank_IsEmpty(string value)
    {
        Assert.Empty(OwnedAnswerKey.ReadSecret(Configuration(value)));
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

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("QA")]
    public void ReadSecret_WithACacheOutsideDevelopment_RequiresASecret(string environment)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            OwnedAnswerKey.ReadSecret(Configuration(null, "redis:6379"), environment));

        Assert.Contains(OwnedAnswerKey.SecretKey, exception.Message);
        Assert.DoesNotContain("redis:6379", exception.Message);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    public void ReadSecret_WithACacheInDevelopment_AllowsThePlainDigest(string environment)
    {
        Assert.Empty(OwnedAnswerKey.ReadSecret(Configuration(null, "redis:6379"), environment));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ReadSecret_WithABlankCache_RequiresNothing(string cache)
    {
        Assert.Empty(OwnedAnswerKey.ReadSecret(Configuration(null, cache), "Production"));
    }

    [Fact]
    public void ReadSecret_WithoutAnEnvironment_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => OwnedAnswerKey.ReadSecret(Configuration(null), null!));
    }

    [Fact]
    public void ReadSecret_WithoutACache_RequiresNothing()
    {
        Assert.Empty(OwnedAnswerKey.ReadSecret(Configuration(null), "Production"));
    }

    [Fact]
    public void ReadSecret_WithACacheAndASecret_ReadsIt()
    {
        Assert.Equal(Secret, OwnedAnswerKey.ReadSecret(Configuration(Convert.ToBase64String(Secret), "redis:6379"), "Production"));
    }

    [Fact]
    public void ReadSecret_RequiredButUnusable_SaysWhy()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            OwnedAnswerKey.ReadSecret(Configuration("AAAA", "redis:6379"), "Production"));

        Assert.Contains("at least", exception.Message);
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
    [InlineData("account{by-email")]
    [InlineData("account}by-email")]
    public void Of_AnAnswerThatCouldBeConfused_Throws(string answer)
    {
        Assert.Throws<ArgumentException>(() => OwnedAnswerKey.Of(answer, "user@example.com"));
    }

    // Built at run time: an attribute's string can't hold a lone surrogate.
    [Theory]
    [InlineData(0xD800, "user", "@example.com")]
    [InlineData(0xDBFF, "user", "@example.com")]
    [InlineData(0xDC00, "user", "@example.com")]
    [InlineData(0xD800, "user@example.com", "")]
    public void Of_AKeyThatIsNotValidText_Throws_WithoutQuotingIt(int surrogate, string before, string after)
    {
        var key = before + (char)surrogate + after;

        var exception = Assert.Throws<ArgumentException>(() => OwnedAnswerKey.Of("account-by-email", key, Secret));

        Assert.Equal("key", exception.ParamName);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("example", exception.Message);
    }

    [Fact]
    public void Of_TheReplacementCharacter_IsItsUtf8()
    {
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("account-by-email\nuser\uFFFD@example.com"u8)[..16]),
            OwnedAnswerKey.Of("account-by-email", "user\uFFFD@example.com").ToString("N"));
    }

    [Fact]
    public void Of_TextBeyondTheBasicPlane_IsItsUtf8()
    {
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("account-by-email\nuser\U0001F600@example.com"u8)[..16]),
            OwnedAnswerKey.Of("account-by-email", "user\U0001F600@example.com").ToString("N"));
    }

    [Fact]
    public void Of_WithoutAKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => OwnedAnswerKey.Of("account-by-email", null!));
    }

    [Theory]
    [InlineData("widget-service", "widget:by-name")]
    [InlineData("widget-service", "widget{by-name")]
    [InlineData("widget-service", "widget}by-name")]
    [InlineData("widget-service", "widget\nby-name")]
    [InlineData("widget-service", " ")]
    [InlineData("widget:service", "widget-by-name")]
    [InlineData("widget{service", "widget-by-name")]
    [InlineData("widget}service", "widget-by-name")]
    [InlineData("", "widget-by-name")]
    public void Template_ANameThatCouldBeConfused_Throws(string service, string answer)
    {
        Assert.Throws<ArgumentException>(() => OwnedAnswerKey.Template(service, answer));
    }

    [Fact]
    public void Template_NoResourceTemplateCanBeAnAnswers()
    {
        Assert.Throws<ArgumentException>(() => OwnedCacheEntry.Template("widget-service", "answer:widget-by-name"));
        Assert.Throws<ArgumentException>(() => OwnedAnswerKey.Template("widget-service", "a:b"));
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

    private static IConfiguration Configuration(string? secret, string? cache = null) => new SecretOnly(secret, cache);

    private sealed class SecretOnly(string? secret, string? cache) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == OwnedAnswerKey.SecretKey ? secret : key == OwnedCacheEntry.ConnectionKey ? cache : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => throw new NotSupportedException();

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
