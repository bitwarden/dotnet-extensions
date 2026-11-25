using Bitwarden.Server.Sdk.Resources.Caching;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// A reader connected as Redis's default user could write the entries it reads: outside Development the host refuses a
/// service cache connection that names no user of the service's own, never echoing the string.
/// </summary>
public sealed class ServiceCacheUserCheckTests
{
    private const string Setting = OwnedCacheEntry.ConnectionKey;

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
    public void Require_AnswersAsEveryCopyDoes(string? configuration, string environment, bool? expected)
    {
        if (expected is { } answer)
        {
            Assert.Equal(answer, ServiceCacheUserCheck.Require(configuration, Setting, environment));
            return;
        }

        // Refused, naming the setting and never echoing the connection string.
        var refusal = Assert.Throws<InvalidOperationException>(() => ServiceCacheUserCheck.Require(configuration, Setting, environment));
        Assert.Contains(Setting, refusal.Message);
        Assert.DoesNotContain("secret-value", refusal.Message);
        Assert.DoesNotContain("localhost", refusal.Message);
        Assert.DoesNotContain("shelf-service", refusal.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Require_WithoutASettingName_Refused(string? setting) =>
        Assert.ThrowsAny<ArgumentException>(() => ServiceCacheUserCheck.Require("localhost:6379", setting!, "Production"));

    [Fact]
    public void Require_WithoutAnEnvironment_Refused() =>
        Assert.Throws<ArgumentNullException>(() => ServiceCacheUserCheck.Require("localhost:6379", Setting, null!));
}
