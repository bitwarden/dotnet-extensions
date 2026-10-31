using Bitwarden.Server.Sdk.Service.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

/// <summary>
/// A chosen count is a value: two are the same count when their rule, period and bucket are.
/// </summary>
public sealed class IpRateLimitCountTests
{
    [Fact]
    public void Equality_IsByRulePeriodAndBucket()
    {
        var rule = new IpRateLimitRule { Endpoint = "post:*", Period = "1m", Limit = 5 };
        var count = new IpRateLimitCount(rule, TimeSpan.FromMinutes(1), "post:*\n1m\n5\n\npost\n/a");

        Assert.Equal(count, new IpRateLimitCount(rule, TimeSpan.FromMinutes(1), "post:*\n1m\n5\n\npost\n/a"));
        Assert.NotEqual(count, count with { Bucket = "post:*\n1m\n5\n\npost\n/b" });
        Assert.NotEqual(count, count with { Period = TimeSpan.FromSeconds(1) });
        // The rule by reference: another rule with the same settings is another count.
        Assert.NotEqual(count, count with { Rule = new IpRateLimitRule { Endpoint = "post:*", Period = "1m", Limit = 5 } });
    }
}
