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

    [Theory]
    [InlineData("localhost:6379,password=secret-value", "Production")]
    [InlineData("localhost:6379,user=default,password=secret-value", "Production")]
    [InlineData("localhost:6379", "Staging")]
    [InlineData("localhost:6379", "Testing")]
    public void RequireUser_OutsideDevelopment_WithoutAUserOfItsOwn_RefusedWithoutTheConnectionString(string configuration, string environment)
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => ServiceCacheAcl.RequireUser(configuration, Setting, environment));

        Assert.Contains(Setting, refusal.Message);
        Assert.DoesNotContain("secret-value", refusal.Message);
        Assert.DoesNotContain("localhost", refusal.Message);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    public void RequireUser_InDevelopment_WithoutAUser_AllowedButSaysSo(string environment) =>
        Assert.False(ServiceCacheAcl.RequireUser("localhost:6379", Setting, environment));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("localhost:6379,user=shelf-service,password=secret-value")]
    public void RequireUser_WithAUserOrNoCache_Allowed(string? configuration) =>
        Assert.True(ServiceCacheAcl.RequireUser(configuration, Setting, "Production"));

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
