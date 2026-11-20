using Bitwarden.Server.Sdk.Resources.Caching;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// A reader connected as Redis's default user could write the entries it reads: outside Development the host refuses a
/// service cache connection that names no user of the service's own, never echoing the string.
/// </summary>
public sealed class ServiceCacheUserCheckTests
{
    private const string Setting = OwnedCacheEntry.ConnectionKey;

    [Theory]
    [InlineData("localhost:6379,password=secret-value", "Production")]
    [InlineData("localhost:6379,user=default,password=secret-value", "Production")]
    [InlineData("localhost:6379", "Staging")]
    [InlineData("localhost:6379,user=,password=secret-value", "Testing")]
    public void Require_OutsideDevelopment_WithoutAUserOfItsOwn_RefusedWithoutTheConnectionString(string configuration, string environment)
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => ServiceCacheUserCheck.Require(configuration, Setting, environment));

        Assert.Contains(Setting, refusal.Message);
        Assert.DoesNotContain("secret-value", refusal.Message);
        Assert.DoesNotContain("localhost", refusal.Message);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    public void Require_InDevelopment_WithoutAUser_AllowedButNotItsOwn(string environment) =>
        Assert.False(ServiceCacheUserCheck.Require("localhost:6379", Setting, environment));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("localhost:6379,user=shelf-service,password=secret-value")]
    public void Require_WithAUserOrNoCache_Allowed(string? configuration) =>
        Assert.True(ServiceCacheUserCheck.Require(configuration, Setting, "Production"));

    // A string the parser refuses names no user, even one that sets a user, so it is refused like one without, and
    // still never echoed.
    [Fact]
    public void Require_Unparseable_OutsideDevelopment_RefusedWithoutTheConnectionString()
    {
        var refusal = Assert.Throws<InvalidOperationException>(
            () => ServiceCacheUserCheck.Require("localhost:6379,user=shelf-service,connectTimeout=secret-value", Setting, "Production"));

        Assert.DoesNotContain("secret-value", refusal.Message);
    }
}
