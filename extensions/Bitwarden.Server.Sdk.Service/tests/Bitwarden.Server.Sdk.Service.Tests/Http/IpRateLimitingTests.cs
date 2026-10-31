using System.Net;
using System.Text;
using System.Text.Json;
using Bitwarden.Server.Sdk.Service.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

// IpRateLimitOptions as AspNetCoreRateLimit 5.0 reads them, with EnableEndpointRateLimiting: per address and
// endpoint, the most restrictive matching rule for each period counted, a 429 with its sentence, off when self-hosted.
public sealed class IpRateLimitingTests
{
    private static readonly IPAddress Browser = IPAddress.Parse("203.0.113.9");

    [Fact]
    public async Task OverTheLimit_RefusedWithStatusSentenceAndRetryAfter()
    {
        var limits = Limits(Rule("post:/connect/token", "1m", 2));

        Assert.Equal(200, await Send(limits, "POST", "/connect/token"));
        Assert.Equal(200, await Send(limits, "POST", "/connect/token"));

        var http = Request("POST", "/connect/token", Browser);
        await new IpRateLimitMiddleware(_ => Task.CompletedTask, limits).InvokeAsync(http);

        Assert.Equal(429, http.Response.StatusCode);
        Assert.Equal("60", http.Response.Headers.RetryAfter.ToString());
        http.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(http.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        var error = body.RootElement.GetProperty("errors")[0];
        Assert.Equal("too-many-requests", error.GetProperty("code").GetString());
        Assert.Equal("Slow down! Too many requests. Try again in 1m.", error.GetProperty("detail").GetString());
    }

    // A service answering in a format of its own writes its own body; the status and Retry-After are the platform's.
    [Fact]
    public async Task AServicesOwnRefusal_WritesTheBody()
    {
        var limits = Limits(new IpRateLimitOptions { GeneralRules = [Rule("post:*", "1s", 1)] }, refusal: new TextRefusal());

        Assert.Equal(200, await Send(limits, "POST", "/a"));
        var http = Request("POST", "/a", Browser);
        await new IpRateLimitMiddleware(_ => Task.CompletedTask, limits).InvokeAsync(http);

        Assert.Equal(429, http.Response.StatusCode);
        Assert.Equal("1", http.Response.Headers.RetryAfter.ToString());
        http.Response.Body.Position = 0;
        using var reader = new StreamReader(http.Response.Body);
        Assert.Equal("Slow down! Too many requests. Try again in 1s.", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EachAddress_HasItsOwnAllowance()
    {
        var limits = Limits(Rule("post:*", "1m", 1));

        Assert.Equal(200, await Send(limits, "POST", "/a", Browser));
        Assert.Equal(429, await Send(limits, "POST", "/a", Browser));
        Assert.Equal(200, await Send(limits, "POST", "/a", IPAddress.Parse("198.51.100.4")));
    }

    [Fact]
    public async Task ARequestMatchingNoRule_IsNotCounted()
    {
        var limits = Limits(Rule("post:/connect/token", "1m", 1));

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(200, await Send(limits, "GET", "/connect/token"));
            Assert.Equal(200, await Send(limits, "POST", "/accounts/prelogin"));
        }
    }

    // EnableEndpointRateLimiting (the default): a general rule counts each method and path apart, as
    // AspNetCoreRateLimit 5.0's counter key adds the request's verb and path.
    [Fact]
    public async Task AGeneralRule_CountsEachEndpointApart()
    {
        var limits = Limits(Rule("*", "1m", 2), Rule("post:*", "1s", 2));

        Assert.Equal(200, await Send(limits, "GET", "/a"));
        Assert.Equal(200, await Send(limits, "GET", "/b"));
        Assert.Equal(200, await Send(limits, "POST", "/a"));
        Assert.Equal(200, await Send(limits, "GET", "/c"));
        Assert.Equal(200, await Send(limits, "GET", "/A/"));
        Assert.Equal(429, await Send(limits, "GET", "/a"));
        Assert.Equal(200, await Send(limits, "POST", "/b"));
        Assert.Equal(200, await Send(limits, "POST", "/b"));
        Assert.Equal(429, await Send(limits, "POST", "/b"));
    }

    // A *:path rule applies to every method, each counted apart.
    [Fact]
    public async Task APathRuleForEveryMethod_CountsEachMethodApart()
    {
        var limits = Limits(Rule("*:/x/*", "1m", 1));

        Assert.Equal(200, await Send(limits, "GET", "/x/1"));
        Assert.Equal(200, await Send(limits, "POST", "/x/1"));
        Assert.Equal(200, await Send(limits, "GET", "/x/2"));
        Assert.Equal(429, await Send(limits, "GET", "/x/1"));
        Assert.Equal(200, await Send(limits, "GET", "/y/1"));
    }

    // Without EnableEndpointRateLimiting: only rules for every request apply, across all paths.
    [Fact]
    public async Task WithoutEndpointRateLimiting_OnlyRulesForEveryRequestApply_AcrossPaths()
    {
        var limits = Limits(new IpRateLimitOptions
        {
            EnableEndpointRateLimiting = false,
            GeneralRules = [Rule("post:*", "1m", 1), Rule("*", "1m", 3)],
        });

        Assert.Equal(200, await Send(limits, "POST", "/a"));
        Assert.Equal(200, await Send(limits, "POST", "/b"));
        Assert.Equal(200, await Send(limits, "GET", "/c"));
        Assert.Equal(429, await Send(limits, "POST", "/d"));
    }

    [Fact]
    public void EndpointRateLimiting_IsOnByDefault() =>
        Assert.True(new IpRateLimitOptions().EnableEndpointRateLimiting);

    // Of the rules a request matches, the most restrictive for each period counts it, and the others don't: a
    // password sign-in with a rule of 10 a minute counts against those 10, not also a general 60.
    [Fact]
    public async Task ForEachPeriod_TheMostRestrictiveMatchingRuleCounts()
    {
        var limits = Limits(Rule("*", "1m", 5), Rule("post:/t", "1m", 2), Rule("*", "1m", 3));

        Assert.Equal(200, await Send(limits, "POST", "/t"));
        Assert.Equal(200, await Send(limits, "POST", "/t"));
        Assert.Equal(429, await Send(limits, "POST", "/t"));
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(200, await Send(limits, "GET", "/x"));
        }

        Assert.Equal(429, await Send(limits, "GET", "/x"));
    }

    // Shortest period first, and counting stops at the first rule over its limit: a refused request doesn't spend the
    // longer periods' allowances.
    [Fact]
    public async Task CountingStops_AtTheFirstRuleOver_ShortestPeriodFirst()
    {
        var time = new FakeTimeProvider();
        var counters = new RecordingCounters(time);
        var limits = Limits(new IpRateLimitOptions { GeneralRules = [Rule("*", "1m", 3), Rule("*", "1s", 1)] }, counters: counters);

        Assert.Equal(200, await Send(limits, "GET", "/a"));
        Assert.Equal(["1s", "1m"], counters.Periods);

        counters.Periods.Clear();
        var http = Request("GET", "/a", Browser);
        await new IpRateLimitMiddleware(_ => Task.CompletedTask, limits).InvokeAsync(http);
        Assert.Equal(429, http.Response.StatusCode);
        Assert.Equal("1", http.Response.Headers.RetryAfter.ToString());
        Assert.Equal(["1s"], counters.Periods);

        // The second's window over, the minute's has counted one request, not two: two more fit, then the minute's refuses.
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(200, await Send(limits, "GET", "/a"));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(200, await Send(limits, "GET", "/a"));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(429, await Send(limits, "GET", "/a"));
        Assert.Equal(["1s", "1s", "1m", "1s", "1m", "1s", "1m"], counters.Periods);
    }

    // In memory, a window opens with its bucket's first request and lasts its period, as AspNetCoreRateLimit's counter.
    [Fact]
    public async Task InMemory_AWindowOpensWithItsFirstRequest_AndClosesAfterItsPeriod()
    {
        var time = new FakeTimeProvider();
        using var counters = new MemoryRateLimitCounters(time);
        var token = TestContext.Current.CancellationToken;

        time.Advance(TimeSpan.FromMilliseconds(700));
        Assert.True(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromSeconds(1), token));
        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromSeconds(1), token));
        Assert.True(await counters.TryTakeAsync("other", 1, TimeSpan.FromSeconds(1), token));
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromSeconds(1), token));

        // Long after, swept windows count afresh.
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(await counters.TryTakeAsync("other", 1, TimeSpan.FromSeconds(1), token));
        Assert.False(await counters.TryTakeAsync("other", 1, TimeSpan.FromSeconds(1), token));
    }

    // A rule that allows none refuses every request.
    [Fact]
    public async Task ALimitOfZero_RefusesEveryRequest()
    {
        var limits = Limits(Rule("get:/z", "1m", 0));

        Assert.Equal(429, await Send(limits, "GET", "/z"));
        Assert.Equal(200, await Send(limits, "GET", "/y"));
    }

    // A token endpoint may also exchange a session: a rule for grants is left out for another grant before the most
    // restrictive is chosen, and keeps its own count, so exchanges spend the general rule's allowance, never sign-ins'.
    [Fact]
    public async Task ARuleForGrants_IsLeftOutBeforeTheChoice_AndKeepsItsOwnCount()
    {
        var signIns = Rule("post:/connect/token", "1m", 2);
        signIns.Grants = ["password"];
        var limits = Limits(Rule("post:*", "1m", 3), signIns);
        const string exchange = "grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Atoken-exchange";

        Assert.Equal(200, await Send(limits, "POST", "/connect/token", form: exchange));
        Assert.Equal(200, await Send(limits, "POST", "/connect/token", form: exchange));
        Assert.Equal(200, await Send(limits, "POST", "/connect/token", form: "grant_type=password"));
        Assert.Equal(200, await Send(limits, "POST", "/connect/token", form: "grant_type=password"));
        Assert.Equal(429, await Send(limits, "POST", "/connect/token", form: "grant_type=password"));
        Assert.Equal(200, await Send(limits, "POST", "/connect/token", form: exchange));
        Assert.Equal(429, await Send(limits, "POST", "/connect/token", form: exchange));
    }

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

    // A token endpoint may also exchange a session for each product's token: a rule may count only the grants it names.
    [Fact]
    public async Task ARuleForGrants_CountsOnlyThoseGrants_AndLeavesTheFormReadable()
    {
        var rule = Rule("post:/connect/token", "1m", 1);
        rule.Grants = ["password"];
        var limits = Limits(rule);

        Assert.Equal(200, await Send(limits, "POST", "/connect/token", form: "grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Atoken-exchange"));
        Assert.Equal(200, await Send(limits, "POST", "/connect/token", form: "grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Atoken-exchange"));
        Assert.Equal(200, await Send(limits, "POST", "/connect/token", form: "grant_type=password&username=a"));

        var http = Request("POST", "/connect/token", Browser, "grant_type=password&username=a");
        string? read = null;
        await new IpRateLimitMiddleware(async next => read = (await next.Request.ReadFormAsync())["username"], limits).InvokeAsync(http);
        Assert.Equal(429, http.Response.StatusCode);
        Assert.Null(read);

        var allowed = Request("POST", "/connect/token", IPAddress.Parse("198.51.100.5"), "grant_type=password&username=b");
        await new IpRateLimitMiddleware(async next => read = (await next.Request.ReadFormAsync())["username"], limits).InvokeAsync(allowed);
        Assert.Equal("b", read);
    }

    // Security finding 02: a repeated grant_type reads as "password,password", which no rule lists, while the
    // endpoint binds the first value and serves a password sign-in. A form naming no single grant counts.
    [Theory]
    [InlineData("grant_type=password&grant_type=password&username=a")]
    [InlineData("grant_type=password&grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Atoken-exchange")]
    [InlineData("username=a")]
    public async Task ARuleForGrants_CountsAFormNamingNoSingleGrant(string form)
    {
        var rule = Rule("post:/connect/token", "1m", 2);
        rule.Grants = ["password"];
        var limits = Limits(rule);

        Assert.Equal(200, await Send(limits, "POST", "/connect/token", form: form));
        Assert.Equal(200, await Send(limits, "POST", "/connect/token", form: "grant_type=password&username=a"));
        Assert.Equal(429, await Send(limits, "POST", "/connect/token", form: form));
    }

    [Fact]
    public async Task ARuleForGrants_CountsARequestWithoutAForm()
    {
        var rule = Rule("post:/connect/token", "1m", 1);
        rule.Grants = ["password"];
        var limits = Limits(rule);

        Assert.Equal(200, await Send(limits, "POST", "/connect/token"));
        Assert.Equal(429, await Send(limits, "POST", "/connect/token", form: "grant_type=password"));
    }

    // A form the server will not read (more keys than its form options allow) names no grant and counts; the endpoint
    // answers it, rather than the limiter failing the request.
    [Fact]
    public async Task ARuleForGrants_CountsAFormTooLargeToRead_AndTheEndpointAnswers()
    {
        var rule = Rule("post:/connect/token", "1m", 1);
        rule.Grants = ["password"];
        var limits = Limits(rule);
        var form = "grant_type=password&" + string.Join('&', Enumerable.Range(0, 1100).Select(key => $"k{key}=v"));

        var http = Request("POST", "/connect/token", Browser, form);
        var answered = false;
        await new IpRateLimitMiddleware(_ => { answered = true; return Task.CompletedTask; }, limits).InvokeAsync(http);

        Assert.True(answered);
        Assert.Equal(200, http.Response.StatusCode);
        Assert.Equal(429, await Send(limits, "POST", "/connect/token", form: "grant_type=password"));
    }

    // An IPv4 client reached over an IPv6 socket is the same client, with the same allowance and the same whitelist.
    [Fact]
    public async Task AnIPv4AddressCarriedInIPv6_IsTheIPv4Address()
    {
        var limits = Limits(new IpRateLimitOptions { IpWhitelist = ["172.18.0.0/16"], GeneralRules = [Rule("post:*", "1m", 1)] });

        Assert.Equal(200, await Send(limits, "POST", "/a", Browser));
        Assert.Equal(429, await Send(limits, "POST", "/a", Browser.MapToIPv6()));
        Assert.Equal(200, await Send(limits, "POST", "/a", IPAddress.Parse("::ffff:172.18.3.4")));
        Assert.Equal(200, await Send(limits, "POST", "/a", IPAddress.Parse("::ffff:172.18.3.4")));
    }

    // Security finding 03: routing serves /connect/token/ as /connect/token, so the limiter counts it the same.
    [Theory]
    [InlineData("/connect/token/")]
    [InlineData("/connect/token//")]
    [InlineData("//connect//token")]
    [InlineData("/Connect/Token/")]
    public async Task ARoutingEquivalentSpelling_CountsAgainstTheSameRule(string spelling)
    {
        var rule = Rule("post:/connect/token", "1m", 2);
        rule.Grants = ["password"];
        var limits = Limits(rule);

        Assert.Equal(200, await Send(limits, "POST", spelling, form: "grant_type=password"));
        Assert.Equal(200, await Send(limits, "POST", "/connect/token", form: "grant_type=password"));
        Assert.Equal(429, await Send(limits, "POST", spelling, form: "grant_type=password"));
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("/", "/")]
    [InlineData("//", "/")]
    [InlineData("/Accounts/Password-Hint/", "/accounts/password-hint")]
    [InlineData("/a//b///c/", "/a/b/c")]
    public void Paths_AreNormalized(string? path, string expected)
    {
        Assert.Equal(expected, IpRateLimiting.Normalized(path));
    }

    [Fact]
    public async Task AWhitelistedNetwork_IsNeverLimited()
    {
        var limits = Limits(new IpRateLimitOptions { IpWhitelist = ["172.18.0.0/16", "10.0.0.7"], GeneralRules = [Rule("*", "1m", 1)] });

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(200, await Send(limits, "POST", "/a", IPAddress.Parse("172.18.3.4")));
            Assert.Equal(200, await Send(limits, "POST", "/a", IPAddress.Parse("10.0.0.7")));
        }
    }

    // EndpointWhitelist: a route counted some other way is never limited here, and counts against no rule.
    [Fact]
    public async Task AWhitelistedEndpoint_IsNeverLimited_AndCountsAgainstNoRule()
    {
        var limits = Limits(new IpRateLimitOptions
        {
            EndpointWhitelist = ["post:/identity/accounts/register/*"],
            GeneralRules = [Rule("post:*", "1m", 1)],
        });

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(200, await Send(limits, "POST", "/identity/accounts/register/finish"));
            Assert.Equal(200, await Send(limits, "POST", "/Identity/Accounts/Register/Finish/"));
        }

        // The whitelisted requests took nothing from the address's allowance.
        Assert.Equal(200, await Send(limits, "POST", "/a"));
        Assert.Equal(429, await Send(limits, "POST", "/a"));
        // A GET is another verb, not whitelisted by a post: pattern.
        var gets = Limits(new IpRateLimitOptions
        {
            EndpointWhitelist = ["post:/identity/accounts/register/*"],
            GeneralRules = [Rule("*", "1m", 1)],
        });
        Assert.Equal(200, await Send(gets, "GET", "/identity/accounts/register/finish"));
        Assert.Equal(429, await Send(gets, "GET", "/identity/accounts/register/finish"));
    }

    [Fact]
    public async Task SelfHosted_OrDisabled_NothingIsLimited()
    {
        var selfHosted = Limits(new IpRateLimitOptions { GeneralRules = [Rule("*", "1m", 1)] }, selfHosted: true);
        var disabled = Limits(new IpRateLimitOptions { Enabled = false, GeneralRules = [Rule("*", "1m", 1)] });

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(200, await Send(selfHosted, "POST", "/a"));
            Assert.Equal(200, await Send(disabled, "POST", "/a"));
        }
    }

    [Theory]
    [InlineData("1s", 1)]
    [InlineData("1m", 60)]
    [InlineData("60m", 3600)]
    [InlineData("1h", 3600)]
    [InlineData("1d", 86400)]
    public void Periods_ReadInSeconds(string text, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), IpRateLimiting.Period(text));
    }

    [Theory]
    [InlineData("/api/v1/*/actions/verify-user", "/api/v1/two-factor-providers/email/actions/verify-user", true)]
    [InlineData("/api/v1/*/actions/verify-user", "/api/v1/two-factor-providers/email/actions/verify-user/x", false)]
    [InlineData("/accounts/prelogin*", "/accounts/prelogin/password", true)]
    [InlineData("/connect/token", "/connect/tokens", false)]
    [InlineData("post:*", "post:/a/b", true)]
    [InlineData("post:*", "*:/a", false)]
    [InlineData("*:/accounts/*", "get:/accounts/x", true)]
    [InlineData("get:/a?c", "get:/abc", true)]
    [InlineData("get:/a?c", "get:/ac", false)]
    [InlineData("*a*b", "xxaxxb", true)]
    [InlineData("*a*b", "xxbxxa", false)]
    public void Patterns_MatchWhole(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, IpRateLimiting.Matches(pattern, path));
    }

    internal static IpRateLimitRule Rule(string endpoint, string period, int limit) =>
        new() { Endpoint = endpoint, Period = period, Limit = limit };

    private static IpRateLimiting Limits(params IpRateLimitRule[] rules) =>
        Limits(new IpRateLimitOptions { GeneralRules = [.. rules] });

    internal static IpRateLimiting Limits(IpRateLimitOptions options, bool selfHosted = false, IIpRateLimitRefusal? refusal = null, IRateLimitCounters? counters = null) =>
        new(
            Options.Create(options),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["globalSettings:selfHosted"] = selfHosted ? "true" : "false" })
                .Build(),
            refusal ?? new PlatformIpRateLimitRefusal(),
            counters ?? new MemoryRateLimitCounters(new FakeTimeProvider()),
            NullLogger<IpRateLimiting>.Instance);

    /// <summary>
    /// Counts in memory, and records each count's period.
    /// </summary>
    private sealed class RecordingCounters(TimeProvider time) : IRateLimitCounters
    {
        private readonly MemoryRateLimitCounters _inner = new(time);

        public List<string> Periods { get; } = [];

        public ValueTask<bool> TryTakeAsync(string bucket, int limit, TimeSpan period, CancellationToken cancellationToken = default)
        {
            Periods.Add(period == TimeSpan.FromSeconds(1) ? "1s" : "1m");
            return _inner.TryTakeAsync(bucket, limit, period, cancellationToken);
        }
    }

    /// <summary>
    /// A service's own refusal body: the sentence alone, as text.
    /// </summary>
    private sealed class TextRefusal : IIpRateLimitRefusal
    {
        public Task WriteAsync(HttpContext http, string message) => http.Response.WriteAsync(message);
    }

    internal static async Task<int> Send(IpRateLimiting limits, string method, string path, IPAddress? from = null, string? form = null)
    {
        var http = Request(method, path, from ?? Browser, form);
        await new IpRateLimitMiddleware(_ => Task.CompletedTask, limits).InvokeAsync(http);

        return http.Response.StatusCode;
    }

    private static DefaultHttpContext Request(string method, string path, IPAddress from, string? form = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = path;
        http.Connection.RemoteIpAddress = from;
        http.Response.Body = new MemoryStream();

        if (form is not null)
        {
            http.Request.ContentType = "application/x-www-form-urlencoded";
            http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(form));
        }

        return http;
    }
}
