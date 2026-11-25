using Bitwarden.Server.Sdk.CryptoPrimitives;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Bitwarden.Server.Sdk.Resources.Caching.Abstractions.Tests;

public sealed class OwnedAnswerKeyTests
{
    private static readonly CacheSecret Secret = new(Enumerable.Repeat((byte)1, CacheSecret.Length).ToArray());

    // Vectors computed independently with Python's hmac and hashlib:
    // hmac.new(secret, b"".join(struct.pack(">Q", len(b)) + b for b in utf8_params), sha256).digest()[:16]
    [Fact]
    public void Of_IsTheFirstSixteenBytesOfTheCacheKey_InOrder()
    {
        var subject = OwnedAnswerKey.Of("account-by-email", "user@example.com", Secret);

        Assert.Equal(Guid.Parse("f88ab9e1-7533-3961-74bc-53a007d35b66"), subject);
        Assert.Equal("f88ab9e17533396174bc53a007d35b66", subject.ToString("N"));
    }

    [Fact]
    public void Of_IsTheCacheKeyOverTheAnswerAndTheKey()
    {
        var derived = CacheKey.DeriveCacheKey(Secret, "account-by-email", "user@example.com");

        Assert.Equal(Convert.ToHexStringLower(derived.AsSpan()[..16]), OwnedAnswerKey.Of("account-by-email", "user@example.com", Secret).ToString("N"));
    }

    [Fact]
    public void Of_WithTheDevelopmentSecret_IsItsVector()
    {
        Assert.Equal("6bc4267737c3bab35ac7c3b85d990618", OwnedAnswerKey.Of("account-by-email", "user@example.com", CacheSecret.Development).ToString("N"));
    }

    [Fact]
    public void Of_WithAnotherSecret_IsAnotherSubject()
    {
        var other = new CacheSecret(Enumerable.Repeat((byte)2, CacheSecret.Length).ToArray());

        Assert.NotEqual(OwnedAnswerKey.Of("account-by-email", "user@example.com", Secret), OwnedAnswerKey.Of("account-by-email", "user@example.com", other));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void ReadSecret_Blank_IsTheDevelopmentSecret(string value)
    {
        Assert.Equal(CacheSecret.Development, OwnedAnswerKey.ReadSecret(Configuration(value), "Production"));
    }

    [Fact]
    public void ReadSecret_Unset_IsTheDevelopmentSecret()
    {
        Assert.Equal(CacheSecret.Development, OwnedAnswerKey.ReadSecret(Configuration(null), "Production"));
    }

    [Fact]
    public void ReadSecret_ReadsBase64()
    {
        Assert.Equal(Secret, OwnedAnswerKey.ReadSecret(Configuration(Secret.ToString()), "Production"));
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    [InlineData("AQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEB")]
    public void ReadSecret_NotAUsableSecret_Throws(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OwnedAnswerKey.ReadSecret(Configuration(value), "Development"));

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
    public void ReadSecret_WithACacheInDevelopment_IsTheDevelopmentSecret(string environment)
    {
        Assert.Equal(CacheSecret.Development, OwnedAnswerKey.ReadSecret(Configuration(null, "redis:6379"), environment));
    }

    [Fact]
    public void ReadSecret_WithACacheOutsideDevelopment_RefusesTheDevelopmentSecret()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            OwnedAnswerKey.ReadSecret(Configuration(CacheSecret.Development.ToString(), "redis:6379"), "Production"));

        Assert.Contains(OwnedAnswerKey.SecretKey, exception.Message);
    }

    [Fact]
    public void ReadSecret_WithACacheInDevelopment_AllowsTheDevelopmentSecret()
    {
        Assert.Equal(CacheSecret.Development, OwnedAnswerKey.ReadSecret(Configuration(CacheSecret.Development.ToString(), "redis:6379"), "Development"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ReadSecret_WithABlankCache_RequiresNothing(string cache)
    {
        Assert.Equal(CacheSecret.Development, OwnedAnswerKey.ReadSecret(Configuration(null, cache), "Production"));
    }

    [Fact]
    public void ReadSecret_WithoutAnEnvironment_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => OwnedAnswerKey.ReadSecret(Configuration(null), null!));
    }

    [Fact]
    public void ReadSecret_WithoutAConfiguration_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => OwnedAnswerKey.ReadSecret(null!, "Production"));
    }

    [Fact]
    public void ReadSecret_WithACacheAndASecret_ReadsIt()
    {
        Assert.Equal(Secret, OwnedAnswerKey.ReadSecret(Configuration(Secret.ToString(), "redis:6379"), "Production"));
    }

    [Fact]
    public void ReadSecret_RequiredButUnusable_SaysWhy()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            OwnedAnswerKey.ReadSecret(Configuration("AAAA", "redis:6379"), "Production"));

        Assert.Contains("base64 of 32 bytes", exception.Message);
    }

    [Fact]
    public void Of_TheSameKeyForTwoAnswers_IsTwoSubjects()
    {
        Assert.NotEqual(OwnedAnswerKey.Of("account-by-email", "user@example.com", Secret), OwnedAnswerKey.Of("invitation-by-email", "user@example.com", Secret));
    }

    [Fact]
    public void Of_DoesNotNormalizeTheKey()
    {
        Assert.NotEqual(OwnedAnswerKey.Of("account-by-email", "user@example.com", Secret), OwnedAnswerKey.Of("account-by-email", "User@Example.com", Secret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Of_WithoutAnAnswer_Throws(string? answer)
    {
        Assert.ThrowsAny<ArgumentException>(() => OwnedAnswerKey.Of(answer!, "user@example.com", Secret));
    }

    [Theory]
    [InlineData("account:by-email")]
    [InlineData("account{by-email")]
    [InlineData("account}by-email")]
    public void Of_AnAnswerThatCouldBeConfused_Throws(string answer)
    {
        Assert.Throws<ArgumentException>(() => OwnedAnswerKey.Of(answer, "user@example.com", Secret));
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
        Assert.Equal("2c9d475dfa243778952e699be8a23b11", OwnedAnswerKey.Of("account-by-email", "user\uFFFD@example.com", Secret).ToString("N"));
    }

    [Fact]
    public void Of_TextBeyondTheBasicPlane_IsItsUtf8()
    {
        Assert.Equal("cdaca37618146a0989e5e19be1ef87be", OwnedAnswerKey.Of("account-by-email", "user\U0001F600@example.com", Secret).ToString("N"));
    }

    // A line feed no longer separates the name from the key: the cache key length-prefixes each.
    [Fact]
    public void Of_ALineFeedMovedBetweenTheAnswerAndTheKey_IsAnotherSubject()
    {
        Assert.NotEqual(OwnedAnswerKey.Of("account-by-email", "a\nb", Secret), OwnedAnswerKey.Of("account-by-email\na", "b", Secret));
    }

    [Fact]
    public void Of_WithoutAKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => OwnedAnswerKey.Of("account-by-email", null!, Secret));
    }

    [Theory]
    [InlineData("widget-service", "widget:by-name")]
    [InlineData("widget-service", "widget{by-name")]
    [InlineData("widget-service", "widget}by-name")]
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
        Assert.Equal("cache-key", OwnedAnswerKey.Digest);
        Assert.Equal("Caching:Owned:AnswerSecret", OwnedAnswerKey.SecretKey);
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
