using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Bitwarden.Server.Sdk.Resources.Caching.Tests;

/// <summary>
/// A service cache connection without a Redis user of the service's own is Redis's default user, which access control
/// does not confine to the service's prefix: outside Development the host refuses it, in Development the service says
/// so, never echoing the string. Each service's user is one rule, which may hold a second password while it is rotated.
/// </summary>
public sealed class ServiceCacheAclTests
{
    private const string Setting = OwnedCacheEntry.ConnectionKey;

    [Theory]
    [InlineData("localhost:6379,defaultDatabase=2,user=shelf-service,password=secret-value", true)]
    [InlineData("localhost:6379,defaultDatabase=2", false)]
    [InlineData("localhost:6379,password=secret-value", false)]
    [InlineData("localhost:6379,user=default,password=secret-value", false)]
    public void NamesAUser_OnlyWithAUserOfItsOwn(string configuration, bool expected) =>
        Assert.Equal(expected, ServiceCacheAcl.NamesAUser(configuration));

    // Shared: this table, comment included, is the same in the tests of each check that refuses a Redis connection
    // naming no user of the service's own: ServiceCacheAcl.RequireUser in Bitwarden.Server.Sdk.Resources.Caching,
    // ServiceCacheUserCheck.Require in Bitwarden.Server.Sdk.Client and RedisUserCheck.Require in
    // Bitwarden.Server.Sdk.Idempotency. Change the copies together: a table changed for one check then fails the others
    // until they change too. Each row is a connection string, the host's environment, and what the check answers: true
    // for a user of the service's own or no connection, false where Development allows one without, and null for a
    // refusal.
    public static TheoryData<string?, string, bool?> RedisUserCases => new()
    {
        // No connection: nothing to check.
        { null, "Production", true },
        { "", "Production", true },
        { " ", "Production", true },
        { null, "Development", true },

        // A user of the service's own. Option names are read without regard to case; user names are compared with
        // it, as Redis compares them, so "Default" is a user of its own, not Redis's default.
        { "localhost:6379,user=shelf-service,password=secret-value", "Production", true },
        { "localhost:6379,User=shelf-service,password=secret-value", "Production", true },
        { "localhost:6379,user=Default,password=secret-value", "Production", true },
        { "localhost:6379,user=shelf-service,password=secret-value", "Development", true },

        // No user, or Redis's default user, outside Development.
        { "localhost:6379", "Production", null },
        { "localhost:6379,password=secret-value", "Production", null },
        { "localhost:6379,user=default,password=secret-value", "Production", null },
        { "localhost:6379,user=,password=secret-value", "Production", null },
        { "localhost:6379,user= ,password=secret-value", "Production", null },

        // A user named twice: the last one is the one the parser keeps.
        { "localhost:6379,user=shelf-service,user=default,password=secret-value", "Production", null },
        { "localhost:6379,user=default,user=shelf-service,password=secret-value", "Production", true },

        // A user only a URI would name: the parser reads no user from one.
        { "redis://shelf-service:secret-value@localhost:6379", "Production", null },

        // A string the parser refuses names no user, even one that sets a user.
        { "localhost:6379,user=shelf-service,connectTimeout=secret-value", "Production", null },

        // Every environment but Development is outside it.
        { "localhost:6379", "Staging", null },
        { "localhost:6379", "Testing", null },
        { "localhost:6379", "", null },

        // In Development, without a user of its own: allowed, but not its own. Compared as IsDevelopment compares it.
        { "localhost:6379", "Development", false },
        { "localhost:6379", "development", false },
        { "localhost:6379,user=default,password=secret-value", "Development", false },
        { "localhost:6379,user=shelf-service,connectTimeout=secret-value", "Development", false },
    };

    [Theory]
    [MemberData(nameof(RedisUserCases))]
    public void RequireUser_AnswersAsEveryCopyDoes(string? configuration, string environment, bool? expected)
    {
        if (expected is { } answer)
        {
            Assert.Equal(answer, ServiceCacheAcl.RequireUser(configuration, Setting, environment));
            return;
        }

        // Refused, naming the setting and never echoing the connection string.
        var refusal = Assert.Throws<InvalidOperationException>(() => ServiceCacheAcl.RequireUser(configuration, Setting, environment));
        Assert.Contains(Setting, refusal.Message);
        Assert.DoesNotContain("secret-value", refusal.Message);
        Assert.DoesNotContain("localhost", refusal.Message);
        Assert.DoesNotContain("shelf-service", refusal.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RequireUser_WithoutASettingName_Refused(string? setting) =>
        Assert.ThrowsAny<ArgumentException>(() => ServiceCacheAcl.RequireUser("localhost:6379", setting!, "Production"));

    [Fact]
    public void RequireUser_WithoutAnEnvironment_Refused() =>
        Assert.Throws<ArgumentNullException>(() => ServiceCacheAcl.RequireUser("localhost:6379", Setting, null!));

    [Fact]
    public void WarnWithoutUser_WarnsWithoutTheConnectionString()
    {
        var logger = new FakeLogger();

        Assert.True(ServiceCacheAcl.WarnWithoutUser("localhost:6379,password=secret-value", logger));

        var record = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.DoesNotContain("secret-value", record.Message);
        Assert.DoesNotContain("localhost", record.Message);
    }

    [Fact]
    public void WarnWithoutUser_IsQuietWithAUser()
    {
        var logger = new FakeLogger();

        Assert.False(ServiceCacheAcl.WarnWithoutUser("localhost:6379,user=shelf-service,password=secret-value", logger));

        Assert.Empty(logger.Collector.GetSnapshot());
    }

    // Applied with ACL SETUSER to a user that exists, the rule leaves exactly the passwords it names: a rotation adds
    // the next password with the current one, then drops the current one with the same rule naming the next alone.
    [Fact]
    public void User_DuringARotation_ResetsAndHoldsBothPasswords()
    {
        var rule = ServiceCacheAcl.User("shelf-service", ["current-secret", "next-secret"], reads: ["library-service"]);

        Assert.StartsWith("shelf-service on resetpass >current-secret >next-secret resetkeys ", rule);
    }

    [Fact]
    public void User_OnePassword_ResetsAndHoldsIt() =>
        Assert.StartsWith("shelf-service on resetpass >only-secret resetkeys ", ServiceCacheAcl.User("shelf-service", "only-secret", reads: []));

    public static TheoryData<string[]> RefusedPasswords => new()
    {
        Array.Empty<string>(),
        new[] { "one", "two", "three" },
        new[] { "same", "same" },
        new[] { "has space" },
        new[] { "ends-a-rule +@all" },
        new[] { " " },
    };

    [Theory]
    [MemberData(nameof(RefusedPasswords))]
    public void User_PasswordsThatAreNotOneOrTwoDistinctWords_Refused(string[] passwords) =>
        Assert.Throws<ArgumentException>(() => ServiceCacheAcl.User("shelf-service", passwords, reads: []));

    // A name is a user's name and the start of a key pattern: a space would start another rule, and '*', '?' or '['
    // would reach other services' prefixes.
    [Theory]
    [InlineData("*")]
    [InlineData("shelf-service +@all")]
    [InlineData("shelf?")]
    [InlineData("shelf[s]")]
    [InlineData("shelf:service")]
    public void Keys_NameThatIsNotAUserName_Refused(string name)
    {
        Assert.Throws<ArgumentException>(() => ServiceCacheAcl.Keys(name, []));
        Assert.Throws<ArgumentException>(() => ServiceCacheAcl.Keys("shelf-service", [name]));
    }

    // A tool writing an ACL file reads these constants, so they must be the prefixes the cache's keys are written under.
    [Fact]
    public void OwnKeysAndReadKeys_AreTheServicesPrefixes()
    {
        Assert.Equal($"%RW~{OwnedCacheEntry.Prefix("shelf-service")}*", ServiceCacheAcl.OwnKeys.Replace("{service}", "shelf-service"));
        Assert.Equal($"%R~{OwnedCacheEntry.Prefix("library-service")}*", ServiceCacheAcl.ReadKeys.Replace("{service}", "library-service"));
        Assert.Equal("%RW~shelf-service:* %R~a-service:* %R~library-service:*", ServiceCacheAcl.Keys("shelf-service", ["library-service", "a-service", "shelf-service"]));
    }

    // SCAN, RANDOMKEY and DBSIZE take no key, so key permissions don't confine them; FUNCTION changes what every user
    // shares. RedisAclTests shows Redis refusing them.
    [Theory]
    [InlineData("-scan")]
    [InlineData("-randomkey")]
    [InlineData("-dbsize")]
    [InlineData("-function")]
    [InlineData("-fcall")]
    [InlineData("-fcall_ro")]
    public void Commands_TakeAwayTheKeylessCommandsTheFamiliesGrant(string rule) =>
        Assert.Contains(rule, ServiceCacheAcl.Commands.Split(' '));
}
