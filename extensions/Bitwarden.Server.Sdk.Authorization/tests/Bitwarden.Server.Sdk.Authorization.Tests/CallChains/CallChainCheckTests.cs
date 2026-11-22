using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Claims;
using Bitwarden.Server.Sdk.Authorization.CallChains;
using Bitwarden.Server.Sdk.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Authorization.Tests.CallChains;

/// <summary>
/// The call chain: removed from every request, believed only from another service, current for the request alone, and
/// a call back into this service while its own request waits counted, logged, put on the span and, when asked, refused,
/// unless the operation declares it; never at the token endpoint or the probes.
/// </summary>
public sealed class CallChainCheckTests
{
    private const string Self = "widget-service";

    private const string Route = "api/v1/widgets/{id}";

    // --- Read --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Read_NoHeader_IsNone()
    {
        Assert.Null(CallChainCheck.Read(Request(Service("gadget-service"), chain: null).Http));
    }

    [Fact]
    public void Read_FromAnotherService_IsTheChain()
    {
        var http = Request(Service("gadget-service"), "gadget-service").Http;

        Assert.Equal(["gadget-service"], CallChainCheck.Read(http));
        Assert.False(http.Request.Headers.ContainsKey(BitwardenHeaders.CallChain));
    }

    // From anybody but another service the header is not believed, and is removed so nothing later reads it.
    public static TheoryData<string, ClaimsPrincipal> NotAService() => new()
    {
        { "person", Principal("web", authenticated: true) },
        { "organization", Principal("organization.0c000000-0000-4000-8000-00000000000c", authenticated: true) },
        { "unauthenticated", Principal("internal.gadget-service", authenticated: false) },
        { "anonymous", new ClaimsPrincipal(new ClaimsIdentity()) },
    };

    [Theory]
    [MemberData(nameof(NotAService))]
    public void Read_FromAnybodyElse_IsNoneAndTheHeaderIsRemoved(string caller, ClaimsPrincipal user)
    {
        var http = Request(user, $"gadget-service,{Self}").Http;

        Assert.Null(CallChainCheck.Read(http));
        Assert.False(http.Request.Headers.ContainsKey(BitwardenHeaders.CallChain), caller);
    }

    // Two values could disagree; neither is believed.
    [Fact]
    public void Read_MoreThanOneValue_IsNoneAndTheHeaderIsRemoved()
    {
        var http = Request(Service("gadget-service"), chain: null).Http;
        http.Request.Headers[BitwardenHeaders.CallChain] = new(["gadget-service", "sprocket-service"]);

        Assert.Null(CallChainCheck.Read(http));
        Assert.False(http.Request.Headers.ContainsKey(BitwardenHeaders.CallChain));
    }

    // Anything but service names is not a chain, so nothing else reaches a log, a span or a metric tag.
    [Theory]
    [InlineData("b0000000-0000-4000-8000-000000000001")]
    [InlineData("someone@example.com")]
    [InlineData("gadget-service,,sprocket-service")]
    [InlineData("")]
    public void Read_AMalformedChainFromAService_IsNoneAndTheHeaderIsRemoved(string chain)
    {
        var http = Request(Service("gadget-service"), chain).Http;

        Assert.Null(CallChainCheck.Read(http));
        Assert.False(http.Request.Headers.ContainsKey(BitwardenHeaders.CallChain));
    }

    // --- InvokeAsync: carried on -------------------------------------------------------------------------------------

    // While the request runs the chain is current, so its calls carry it on; afterwards it is gone.
    [Fact]
    public async Task Invoke_TheChainIsCurrentForTheRequestOnly()
    {
        using var request = Request(Service("gadget-service"), "gadget-service");
        IReadOnlyList<string>? seen = null;

        await new CallChainCheck(_ =>
        {
            seen = CallChain.Current;
            return Task.CompletedTask;
        }).InvokeAsync(request.Http);

        Assert.Equal(["gadget-service"], seen);
        Assert.Null(CallChain.Current);
    }

    // A person's chain, even naming this service with refusal on, is dropped: it can't start a refusal.
    [Fact]
    public async Task Invoke_APersonsChainNamingThisService_PassesWithNoChain()
    {
        using var request = Request(Principal("web", authenticated: true), $"gadget-service,{Self}", refuse: true);
        var passed = false;
        IReadOnlyList<string>? seen = ["unset"];

        await new CallChainCheck(_ =>
        {
            passed = true;
            seen = CallChain.Current;
            return Task.CompletedTask;
        }).InvokeAsync(request.Http);

        Assert.True(passed);
        Assert.Null(seen);
        Assert.Empty(request.Reentries.Measurements);
    }

    [Fact]
    public async Task Invoke_AChain_IsTaggedOnTheSpan()
    {
        using var request = Request(Service("gadget-service"), "sprocket-service,gadget-service");
        using var span = new Activity("request").Start();

        await new CallChainCheck(_ => Task.CompletedTask).InvokeAsync(request.Http);

        Assert.Equal("sprocket-service,gadget-service", span.GetTagItem(CallChainCheck.ChainSpanTag));
        Assert.Null(span.GetTagItem(CallChainCheck.ReentrySpanTag));
    }

    // Another service's name repeated isn't this service's re-entry.
    [Fact]
    public async Task Invoke_AChainNotNamingThisService_ServedWithRefusalOn()
    {
        using var request = Request(Service("gadget-service"), "gadget-service,sprocket-service,gadget-service", refuse: true);
        var passed = false;

        await new CallChainCheck(_ =>
        {
            passed = true;
            return Task.CompletedTask;
        }).InvokeAsync(request.Http);

        Assert.True(passed);
        Assert.Empty(request.Reentries.Measurements);
        Assert.Empty(request.Logs.Recorded);
    }

    // --- InvokeAsync: a re-entry -------------------------------------------------------------------------------------

    // Off by default: counted, logged as a warning with the loop, put on the span, and served.
    [Fact]
    public async Task Invoke_CalledBackWhileItsOwnRequestWaits_CountedLoggedTaggedAndServed()
    {
        using var request = Request(Service("gadget-service"), $"{Self},gadget-service");
        request.Http.SetEndpoint(Endpoint());
        using var span = new Activity("request").Start();
        var passed = false;

        await new CallChainCheck(_ =>
        {
            passed = true;
            return Task.CompletedTask;
        }).InvokeAsync(request.Http);

        var loop = $"{Self},gadget-service,{Self}";
        Assert.True(passed);
        var counted = Assert.Single(request.Reentries.Measurements);
        Assert.Equal(loop, counted[CallChainMetrics.ChainTag]);
        Assert.Equal(Route, counted[CallChainMetrics.RouteTag]);
        Assert.Equal(false, counted[CallChainMetrics.AcceptedTag]);
        var warning = Assert.Single(request.Logs.Recorded);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.StartsWith("Re-entry: GetWidget", warning.Message, StringComparison.Ordinal);
        Assert.Contains(loop, warning.Message, StringComparison.Ordinal);
        Assert.Equal(true, span.GetTagItem(CallChainCheck.ReentrySpanTag));
    }

    // With no endpoint matched the route is named as such, never as the request's path.
    [Fact]
    public async Task Invoke_ReentryWithNoEndpoint_CountedAsUnmatched()
    {
        using var request = Request(Service("gadget-service"), $"{Self},gadget-service");
        request.Http.Request.Path = "/api/v1/widgets/b0000000-0000-4000-8000-000000000001";

        await new CallChainCheck(_ => Task.CompletedTask).InvokeAsync(request.Http);

        Assert.Equal("(unmatched)", Assert.Single(request.Reentries.Measurements)[CallChainMetrics.RouteTag]);
    }

    // With CallChain:RefuseReentry: refused before the operation runs, still counted and logged.
    [Fact]
    public async Task Invoke_CalledBackWithRefusalOn_RefusedBeforeTheOperation()
    {
        using var request = Request(Service("gadget-service"), $"gadget-service,{Self},sprocket-service", refuse: true);
        request.Http.SetEndpoint(Endpoint());
        var passed = false;

        var refusal = await Assert.ThrowsAsync<CallReentryException>(() => new CallChainCheck(_ =>
        {
            passed = true;
            return Task.CompletedTask;
        }).InvokeAsync(request.Http));

        Assert.False(passed);
        Assert.Contains("GetWidget", refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"gadget-service,{Self},sprocket-service,{Self}", refusal.Message, StringComparison.Ordinal);
        Assert.Single(request.Reentries.Measurements);
        Assert.Equal(LogLevel.Warning, Assert.Single(request.Logs.Recorded).Level);
    }

    // Declared on the operation: counted as accepted, logged at information with the reason, never refused.
    [Fact]
    public async Task Invoke_AcceptedReentryWithRefusalOn_CountedAsAcceptedAndServed()
    {
        const string Reason = "The widget's price is read back before the order commits.";
        using var request = Request(Service("gadget-service"), $"{Self},gadget-service", refuse: true);
        request.Http.SetEndpoint(Endpoint(new AcceptsReentryAttribute(Reason)));
        IReadOnlyList<string>? seen = null;

        await new CallChainCheck(_ =>
        {
            seen = CallChain.Current;
            return Task.CompletedTask;
        }).InvokeAsync(request.Http);

        Assert.Equal([Self, "gadget-service"], seen);
        Assert.Equal(true, Assert.Single(request.Reentries.Measurements)[CallChainMetrics.AcceptedTag]);
        var information = Assert.Single(request.Logs.Recorded);
        Assert.Equal(LogLevel.Information, information.Level);
        Assert.Contains(Reason, information.Message, StringComparison.Ordinal);
        Assert.Contains($"{Self},gadget-service,{Self}", information.Message, StringComparison.Ordinal);
    }

    // --- The platform's plumbing -------------------------------------------------------------------------------------

    // A token or context exchange, and a probe, is never a re-entry. The same request at any other path is refused.
    [Theory]
    [InlineData("/connect/token", false)]
    [InlineData("/connect/token/", false)]
    [InlineData("/CONNECT/TOKEN", false)]
    [InlineData("/api/v1/health", false)]
    [InlineData("/api/v1/health/live", false)]
    [InlineData("/api/v1/readiness", false)]
    [InlineData("/api/v1/startup", false)]
    [InlineData("/connect/tokens", true)]
    [InlineData("/api/v1/healthy", true)]
    [InlineData("/api/v1/widgets", true)]
    public async Task Invoke_AtThePlatformsPlumbing_NeverAReentry(string path, bool refused)
    {
        using var request = Request(Service("gadget-service"), $"{Self},gadget-service", refuse: true);
        request.Http.Request.Path = path;
        var passed = false;
        var check = new CallChainCheck(_ =>
        {
            passed = true;
            return Task.CompletedTask;
        });

        if (refused)
        {
            await Assert.ThrowsAsync<CallReentryException>(() => check.InvokeAsync(request.Http));
            Assert.False(passed);
        }
        else
        {
            await check.InvokeAsync(request.Http);
            Assert.True(passed);
            Assert.Empty(request.Reentries.Measurements);
        }
    }

    // --- Helpers -----------------------------------------------------------------------------------------------------

    private static ClaimsPrincipal Principal(string client, bool authenticated) =>
        new(new ClaimsIdentity([new Claim(InternalCaller.ClientClaim, client)], authenticated ? "Bearer" : null));

    private static ClaimsPrincipal Service(string name) => Principal($"internal.{name}", authenticated: true);

    private static RouteEndpoint Endpoint(params object[] metadata) =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse(Route), 0, new EndpointMetadataCollection(metadata), "GetWidget");

    /// <summary>
    /// A request to this service as the check sees it, after authentication, with what it counts and logs.
    /// </summary>
    private static Arranged Request(ClaimsPrincipal user, string? chain, bool refuse = false)
    {
        var logs = new Logs();
        var services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(logs))
            .AddMetrics()
            .AddSingleton<IServiceIdentity>(new Identity(Self))
            .AddSingleton<CallChainMetrics>()
            .Configure<CallChainOptions>(options => options.RefuseReentry = refuse)
            .BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services, User = user };
        if (chain is not null)
        {
            http.Request.Headers[BitwardenHeaders.CallChain] = chain;
        }

        return new Arranged(http, logs, new Reentries(services.GetRequiredService<IMeterFactory>()));
    }

    private sealed record Arranged(DefaultHttpContext Http, Logs Logs, Reentries Reentries) : IDisposable
    {
        public void Dispose()
        {
            Reentries.Dispose();
            ((IDisposable)Http.RequestServices).Dispose();
        }
    }

    private sealed record Identity(string Name) : IServiceIdentity
    {
        public string Title => Name;
    }

    /// <summary>
    /// Every re-entry one service provider counts.
    /// </summary>
    private sealed class Reentries : IDisposable
    {
        private readonly MeterListener _listener = new();

        public Reentries(IMeterFactory factory)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter.Scope, factory) && instrument.Name == CallChainMetrics.ReentriesName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                var recorded = new Dictionary<string, object?>();
                foreach (var tag in tags)
                {
                    recorded[tag.Key] = tag.Value;
                }

                Measurements.Add(recorded);
            });
            _listener.Start();
        }

        public List<Dictionary<string, object?>> Measurements { get; } = [];

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>
    /// Every record the check logs.
    /// </summary>
    private sealed class Logs : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Recorded { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(Logs logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                logs.Recorded.Add((logLevel, formatter(state, exception)));
        }
    }
}
