using Bitwarden.Server.Sdk.Service.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

/// <summary>
/// One limit's defaults: every request, a minute, no allowance until one is given, and every grant counted.
/// </summary>
public sealed class IpRateLimitRuleTests
{
    [Fact]
    public void Defaults_EveryRequestPerMinute_NoGrantsNamed()
    {
        var rule = new IpRateLimitRule();

        Assert.Equal("*", rule.Endpoint);
        Assert.Equal("1m", rule.Period);
        Assert.Empty(rule.Grants);
    }

    // A rule configured without a limit allows nothing: it refuses every request it counts.
    [Fact]
    public void WithoutALimit_EveryRequestIsRefused()
    {
        var rule = new IpRateLimitRule();
        var rules = new IpRateLimitRules([rule], enableEndpointRateLimiting: true);

        Assert.Equal(0, rule.Limit);
        Assert.Same(rule, Assert.Single(rules.Counting("GET", "/a")).Rule);
    }
}
