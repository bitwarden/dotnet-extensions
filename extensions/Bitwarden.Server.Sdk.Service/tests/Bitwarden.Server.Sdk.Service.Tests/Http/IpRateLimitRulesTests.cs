using Bitwarden.Server.Sdk.Service.Http;
using Microsoft.Extensions.Time.Testing;
using static Bitwarden.Server.Sdk.Service.Tests.Http.IpRateLimitingTests;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

/// <summary>
/// Which rules count a request, in which order and in which bucket: the most restrictive for each period, shortest
/// period first, a rule for grants left out for another grant, and counting stopped at the first rule over.
/// </summary>
public sealed class IpRateLimitRulesTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Counting_NamesTheRulesShortestPeriodFirst_WithTheEndpointInTheBucket()
    {
        var rules = new IpRateLimitRules(
            [Rule("post:*", "1m", 60), Rule("post:*", "1s", 5), Rule("get:*", "1m", 200), Rule("post:/accounts/password-hint", "60m", 5)],
            enableEndpointRateLimiting: true);

        var counting = rules.Counting("POST", "/Accounts/Password-Hint/");

        Assert.Equal(["1s", "1m", "60m"], counting.Select(count => count.Rule.Period));
        Assert.All(counting, count => Assert.EndsWith("\npost\n/accounts/password-hint", count.Bucket, StringComparison.Ordinal));
        Assert.Empty(rules.Counting("PATCH", "/accounts/password-hint"));
    }

    [Fact]
    public void NoRules_NothingToApply()
    {
        var rules = new IpRateLimitRules([], enableEndpointRateLimiting: true);

        Assert.False(rules.Any);
        Assert.Empty(rules.Counting("GET", "/a"));
    }

    // Without endpoint limiting only the rules for every request are read, and their bucket carries no endpoint.
    [Fact]
    public void WithoutEndpointRateLimiting_OnlyRulesForEveryRequest_CountingAcrossEndpoints()
    {
        var rules = new IpRateLimitRules([Rule("post:*", "1s", 1), Rule("*", "1m", 10)], enableEndpointRateLimiting: false);

        var post = Assert.Single(rules.Counting("POST", "/a"));
        var get = Assert.Single(rules.Counting("GET", "/b"));

        Assert.False(rules.EnableEndpointRateLimiting);
        Assert.Equal("*", post.Rule.Endpoint);
        Assert.Equal(post.Bucket, get.Bucket);
    }

    [Fact]
    public void ARuleWithAPeriodNotInItsForm_FailsWhenRead() =>
        Assert.Throws<InvalidOperationException>(() => new IpRateLimitRules([Rule("*", "a minute", 1)], enableEndpointRateLimiting: true));

    // Two rules differing only in their limit, or their grants, keep counts of their own.
    [Fact]
    public void RulesDifferingInLimitOrGrants_HaveBucketsOfTheirOwn()
    {
        var signIns = Rule("post:/connect/token", "1m", 5);
        signIns.Grants = ["Password", "authorization_code"];
        var reordered = Rule("post:/connect/token", "1m", 5);
        reordered.Grants = ["authorization_code", "password"];

        static string Bucket(IpRateLimitRule rule) =>
            Assert.Single(new IpRateLimitRules([rule], enableEndpointRateLimiting: true).Counting("POST", "/connect/token")).Bucket;

        Assert.NotEqual(Bucket(Rule("post:/connect/token", "1m", 5)), Bucket(Rule("post:/connect/token", "1m", 6)));
        Assert.NotEqual(Bucket(Rule("post:/connect/token", "1m", 5)), Bucket(signIns));
        // The grants' case and order don't matter.
        Assert.Equal(Bucket(signIns), Bucket(reordered));
    }

    [Fact]
    public void NamesGrants_OnlyWhereAMatchingRuleNamesThem()
    {
        var signIns = Rule("post:/connect/token", "1m", 5);
        signIns.Grants = ["password"];
        var rules = new IpRateLimitRules([Rule("*", "1m", 60), signIns], enableEndpointRateLimiting: true);

        Assert.True(rules.NamesGrants("POST", "/connect/token"));
        Assert.False(rules.NamesGrants("GET", "/connect/token"));
        Assert.False(rules.NamesGrants("POST", "/accounts"));
    }

    // A rule for grants counts the grants it names, any case, and a request naming no single grant; it leaves out
    // only a request naming another grant.
    [Theory]
    [InlineData("password", true)]
    [InlineData("PASSWORD", true)]
    [InlineData(null, true)]
    [InlineData("client_credentials", false)]
    public void ARuleForGrants_LeavesOutOnlyAnotherGrant(string? grant, bool counted)
    {
        var signIns = Rule("post:/connect/token", "1m", 5);
        signIns.Grants = ["password"];
        var rules = new IpRateLimitRules([signIns], enableEndpointRateLimiting: true);

        Assert.Equal(counted, rules.Counting("POST", "/connect/token", grant).Count == 1);
    }

    // Of two rules for one period, the lower limit counts; at the same limit, the endpoint pattern sorting first.
    [Fact]
    public void ForAPeriod_TheLowestLimitCounts_ThenTheFirstPattern()
    {
        var rules = new IpRateLimitRules([Rule("post:*", "1m", 3), Rule("*", "1m", 3), Rule("*", "1m", 9)], enableEndpointRateLimiting: true);

        Assert.Equal("*", Assert.Single(rules.Counting("POST", "/a")).Rule.Endpoint);
        Assert.Equal(3, Assert.Single(rules.Counting("POST", "/a")).Rule.Limit);
    }

    // A rule written for a spelling routing serves as another still counts it, as sent or normalized.
    [Theory]
    [InlineData("post:/a//b", "/a//b")]
    [InlineData("post:/a/b", "/a//b/")]
    public void ARule_MatchesThePathAsSentOrNormalized(string endpoint, string path)
    {
        var rules = new IpRateLimitRules([Rule(endpoint, "1m", 1)], enableEndpointRateLimiting: true);

        Assert.Single(rules.Counting("POST", path));
    }

    // No method and no path: the root, matched only by rules for every method.
    [Fact]
    public void NoMethodOrPath_IsTheRoot()
    {
        var rules = new IpRateLimitRules([Rule("*:/", "1m", 1), Rule("get:*", "1m", 1)], enableEndpointRateLimiting: true);

        Assert.Equal("*:/", Assert.Single(rules.Counting(null, null)).Rule.Endpoint);
    }

    // Counting stops at the first rule over its limit: the longer periods are not spent.
    [Fact]
    public async Task TryTake_StopsAtTheFirstRuleOver()
    {
        var counters = new Recording();
        var rules = new IpRateLimitRules([Rule("*", "1h", 10), Rule("*", "1s", 1)], enableEndpointRateLimiting: true);

        Assert.Null(await rules.TryTakeAsync(counters, "203.0.113.9", "GET", "/a", cancellationToken: Token));
        var over = await rules.TryTakeAsync(counters, "203.0.113.9", "GET", "/a", cancellationToken: Token);

        Assert.Equal(TimeSpan.FromSeconds(1), over?.Period);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromHours(1), TimeSpan.FromSeconds(1)], counters.Periods);
    }

    // A limit of zero refuses without counting; the address is the last part of the counted bucket.
    [Fact]
    public async Task TryTake_ALimitOfZero_RefusesWithoutCounting_AndTheAddressEndsTheBucket()
    {
        var counters = new Recording();
        var blocked = new IpRateLimitRules([Rule("get:/z", "1m", 0)], enableEndpointRateLimiting: true);
        var open = new IpRateLimitRules([Rule("get:/z", "1m", 1)], enableEndpointRateLimiting: true);

        Assert.NotNull(await blocked.TryTakeAsync(counters, "203.0.113.9", "GET", "/z", cancellationToken: Token));
        Assert.Empty(counters.Buckets);

        Assert.Null(await open.TryTakeAsync(counters, "203.0.113.9", "GET", "/z", cancellationToken: Token));
        Assert.EndsWith("\n203.0.113.9", Assert.Single(counters.Buckets), StringComparison.Ordinal);
    }

    /// <summary>
    /// Counts in memory, recording each count's bucket and period.
    /// </summary>
    private sealed class Recording : IRateLimitCounters
    {
        private readonly MemoryRateLimitCounters _inner = new(new FakeTimeProvider());

        public List<string> Buckets { get; } = [];

        public List<TimeSpan> Periods { get; } = [];

        public ValueTask<bool> TryTakeAsync(string bucket, int limit, TimeSpan period, CancellationToken cancellationToken = default)
        {
            Buckets.Add(bucket);
            Periods.Add(period);
            return _inner.TryTakeAsync(bucket, limit, period, cancellationToken);
        }
    }
}
