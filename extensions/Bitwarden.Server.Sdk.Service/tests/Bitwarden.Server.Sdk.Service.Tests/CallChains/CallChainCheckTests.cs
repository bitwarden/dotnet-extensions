using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Claims;
using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Authorization.CallChains;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Service.Tests.CallChains;

/// <summary>
/// The call chain: carried on from another service with this one added, never believed from anybody else, and a call
/// back into this service while its own request waits counted, logged, put on the span and, when asked, refused,
/// unless the operation declares it; never at the token endpoint or the probes.
/// </summary>
/// <remarks>
/// Compiled twice, as every test here is: with the request delegate generator, and without it
/// (Bitwarden.Server.Sdk.Service.MinimalApi.Tests).
/// </remarks>
public sealed class CallChainCheckTests
{
    private const string Self = SessionHost.ServiceName;

    private static readonly Dictionary<string, string?> Refusing = new() { ["CallChain:RefuseReentry"] = "true" };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // --- Carried on --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Downstream_FromAServiceWithAChain_CarriesItOnWithThisServiceLast()
    {
        await using var host = await SessionHost.StartAsync(productService: false);

        using var response = await Send(host, "downstream", ServiceToken(), "api-service,identity-service");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            $"api-service,identity-service,{Self}",
            Assert.Single(host.Recorded.Downstream)[BitwardenHeaders.CallChain]);
    }

    [Fact]
    public async Task Downstream_FromAServiceWithNoChain_StartsOneWithThisService()
    {
        await using var host = await SessionHost.StartAsync(productService: false);

        using var response = await Send(host, "downstream", ServiceToken(), chain: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Self, Assert.Single(host.Recorded.Downstream)[BitwardenHeaders.CallChain]);
    }

    // --- Never believed from anybody else ----------------------------------------------------------------------------

    // A person's request, at a product service or a domain service a browser can reach: the chain they sent is
    // dropped, so a forged one can neither start a refusal nor reach a log, a span or a metric. Even naming this
    // service, with refusal on.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Downstream_APersonsRequestWithAChain_CarriesOnlyThisService(bool productService)
    {
        await using var host = await SessionHost.StartAsync(productService, Refusing);

        using var response = await Send(host, "downstream", SessionHost.Token(Guid.NewGuid(), SessionHost.Scope), $"forged-service,{Self}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Self, Assert.Single(host.Recorded.Downstream)[BitwardenHeaders.CallChain]);
    }

    // From a service, anything but service names is not a chain, and is read as none.
    [Theory]
    [InlineData("b0000000-0000-4000-8000-000000000001")]
    [InlineData("someone@example.com")]
    [InlineData("identity-service,,billing-service")]
    public async Task Downstream_AMalformedChainFromAService_ReadAsNone(string chain)
    {
        await using var host = await SessionHost.StartAsync(productService: false);

        using var response = await Send(host, "downstream", ServiceToken(), chain);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Self, Assert.Single(host.Recorded.Downstream)[BitwardenHeaders.CallChain]);
    }

    [Fact]
    public void Read_ACallersHeader_IsRemovedWhoeverSentIt()
    {
        var person = Request("web", Self);
        var service = Request("internal.identity-service", "identity-service");

        Assert.Null(CallChainCheck.Read(person));
        Assert.Equal(["identity-service"], CallChainCheck.Read(service));
        Assert.False(person.Request.Headers.ContainsKey(BitwardenHeaders.CallChain));
        Assert.False(service.Request.Headers.ContainsKey(BitwardenHeaders.CallChain));
    }

    // --- A re-entry --------------------------------------------------------------------------------------------------

    // Off by default: counted, logged as a warning with the chain, put on the span, and served.
    [Fact]
    public async Task Downstream_CalledBackWhileItsOwnRequestWaits_CountedLoggedAndServed()
    {
        var logs = new Logs();
        await using var host = await SessionHost.StartAsync(productService: false, services: logs.Register);
        using var reentries = new Reentries(host);
        using var spans = new Spans();

        using var response = await Send(host, "downstream", ServiceToken(), $"{Self},organization-service");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var loop = $"{Self},organization-service,{Self}";
        var counted = Assert.Single(reentries.Recorded);
        Assert.Equal(loop, counted[CallChainMetrics.ChainTag]);
        Assert.Equal(false, counted[CallChainMetrics.AcceptedTag]);
        Assert.Equal("api/v1/probes/downstream", counted[CallChainMetrics.RouteTag]);
        var warning = Assert.Single(logs.Recorded, entry => entry.Level == LogLevel.Warning && entry.Message.StartsWith("Re-entry", StringComparison.Ordinal));
        Assert.Contains(loop, warning.Message, StringComparison.Ordinal);
        var span = await spans.WaitForAsync(span => (span.GetTagItem(CallChainCheck.ChainSpanTag) as string) == $"{Self},organization-service");
        Assert.Equal(true, span.GetTagItem(CallChainCheck.ReentrySpanTag));
    }

    // With CallChain:RefuseReentry, as in tests and Development: refused before the operation runs.
    [Fact]
    public async Task Downstream_CalledBackWithRefusalOn_Refused500()
    {
        await using var host = await SessionHost.StartAsync(productService: false, Refusing);

        using var response = await Send(host, "downstream", ServiceToken(), $"organization-service,{Self},billing-service");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(host.Recorded.Downstream);
    }

    // Another service's name repeated isn't this service's re-entry.
    [Fact]
    public async Task Downstream_AChainNotNamingThisService_ServedWithRefusalOn()
    {
        await using var host = await SessionHost.StartAsync(productService: false, Refusing);
        using var reentries = new Reentries(host);

        using var response = await Send(host, "downstream", ServiceToken(), "identity-service,organization-service,identity-service");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(reentries.Recorded);
    }

    // Declared on the operation: counted as accepted, logged at information with the reason, never refused.
    [Fact]
    public async Task AcceptedProbe_CalledBackWithRefusalOn_CountedAsAcceptedAndServed()
    {
        var logs = new Logs();
        await using var host = await SessionHost.StartAsync(productService: false, Refusing, logs.Register);
        using var reentries = new Reentries(host);

        using var response = await Send(host, "reentry-accepted", ServiceToken(), $"{Self},organization-service");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"{Self},organization-service,{Self}", Assert.Single(host.Recorded.Downstream)[BitwardenHeaders.CallChain]);
        Assert.Equal(true, Assert.Single(reentries.Recorded)[CallChainMetrics.AcceptedTag]);
        Assert.DoesNotContain(logs.Recorded, entry => entry.Level >= LogLevel.Warning && entry.Message.Contains("re-entry", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logs.Recorded, entry => entry.Level == LogLevel.Information && entry.Message.Contains(Probes.ReentryReason, StringComparison.Ordinal));
    }

    // --- The platform's plumbing -------------------------------------------------------------------------------------

    [Fact]
    public async Task HealthProbe_FromAServiceNamingThisOne_ServedWithRefusalOn()
    {
        await using var host = await SessionHost.StartAsync(productService: false, Refusing);
        using var reentries = new Reentries(host);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/health");
        request.Headers.Authorization = new("Bearer", ServiceToken());
        request.Headers.Add(BitwardenHeaders.CallChain, $"{Self},identity-service");
        using var response = await host.Client().SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(reentries.Recorded);
    }

    // Identity's token endpoint: a token or context exchange while a request of Identity's own waits is the platform's
    // plumbing, never a re-entry. The same request at any other path is refused.
    [Theory]
    [InlineData("/connect/token", false)]
    [InlineData("/connect/token/", false)]
    [InlineData("/CONNECT/TOKEN", false)]
    [InlineData("/api/v1/readiness", false)]
    [InlineData("/api/v1/startup", false)]
    [InlineData("/connect/tokens", true)]
    [InlineData("/api/v1/accounts", true)]
    public async Task Invoke_AtThePlatformsPlumbing_NeverAReentry(string path, bool refused)
    {
        var http = Request("internal.organization-service", "identity-service,organization-service", "identity-service");
        http.Request.Path = path;
        var passed = false;
        var check = new CallChainCheck(_ =>
        {
            passed = true;
            return Task.CompletedTask;
        });

        if (refused)
        {
            await Assert.ThrowsAsync<CallReentryException>(() => check.InvokeAsync(http));
            Assert.False(passed);
        }
        else
        {
            await check.InvokeAsync(http);
            Assert.True(passed);
        }
    }

    // While the request runs the chain is current, so its calls carry it on; afterwards it is gone.
    [Fact]
    public async Task Invoke_TheChainIsCurrentForTheRequestOnly()
    {
        var http = Request("internal.identity-service", "identity-service", Self);
        IReadOnlyList<string>? seen = null;

        await new CallChainCheck(_ =>
        {
            seen = CallChain.Current;
            return Task.CompletedTask;
        }).InvokeAsync(http);

        Assert.Equal(["identity-service"], seen);
        Assert.Null(CallChain.Current);
    }

    [Fact]
    public void AcceptsReentry_SaysWhy()
    {
        Assert.Equal("Why.", new AcceptsReentryAttribute("Why.").Reason);
        Assert.Throws<ArgumentException>(() => new AcceptsReentryAttribute(" "));

        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(AcceptsReentryAttribute), typeof(AttributeUsageAttribute))!;
        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }

    [Fact]
    public void Options_RefusalIsOffByDefault()
    {
        Assert.False(new CallChainOptions().RefuseReentry);
    }

    // --- Helpers -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// A service's token, as <c>internal.some-service</c>.
    /// </summary>
    private static string ServiceToken() => SessionHost.Token(user: null, SessionHost.Scope);

    private static async Task<HttpResponseMessage> Send(SessionHost host, string probe, string token, string? chain)
    {
        using var request = ProductServiceAttributeTests.Get($"/api/v1/probes/{probe}", token);

        if (chain is not null)
        {
            request.Headers.TryAddWithoutValidation(BitwardenHeaders.CallChain, chain);
        }

        return await host.Client().SendAsync(request, Token);
    }

    /// <summary>
    /// A request as the check sees it, after authentication, with refusal on.
    /// </summary>
    private static DefaultHttpContext Request(string client, string chain, string service = "some-service")
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddMetrics()
            .AddSingleton<IServiceIdentity>(new Identity(service))
            .AddSingleton<CallChainMetrics>()
            .Configure<CallChainOptions>(options => options.RefuseReentry = true)
            .BuildServiceProvider();

        var http = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(InternalCaller.ClientClaim, client)], "Bearer")),
        };
        http.Request.Headers[BitwardenHeaders.CallChain] = chain;

        return http;
    }

    private sealed record Identity(string Name) : IServiceIdentity
    {
        public string Title => Name;
    }

    /// <summary>
    /// Every re-entry one host counts.
    /// </summary>
    private sealed class Reentries : IDisposable
    {
        private readonly MeterListener _listener = new();

        public Reentries(SessionHost host)
        {
            var meters = host.Resolve<IMeterFactory>();

            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == CallChainMetrics.MeterName && ReferenceEquals(instrument.Meter.Scope, meters)
                    && instrument.Name == CallChainMetrics.ReentriesName)
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

                Recorded.Enqueue(recorded);
            });
            _listener.Start();
        }

        public ConcurrentQueue<Dictionary<string, object?>> Recorded { get; } = new();

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>
    /// Every request span that stops while it listens.
    /// </summary>
    private sealed class Spans : IDisposable
    {
        private readonly ActivityListener _listener;

        public Spans()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = Stopped.Enqueue,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public ConcurrentQueue<Activity> Stopped { get; } = new();

        /// <summary>
        /// The span matching, once it has stopped: the server may stop it just after the answer is sent.
        /// </summary>
        public async Task<Activity> WaitForAsync(Func<Activity, bool> matching)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (Stopped.FirstOrDefault(matching) is { } found)
                {
                    return found;
                }

                await Task.Delay(50, Token);
            }

            throw new Xunit.Sdk.XunitException("No matching request span stopped.");
        }

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>
    /// Every log record one host writes.
    /// </summary>
    private sealed class Logs : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Recorded { get; } = new();

        public void Register(IServiceCollection services) => services.AddSingleton<ILoggerProvider>(this);

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(Logs logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                logs.Recorded.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
