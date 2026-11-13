using System.Collections.Concurrent;
using System.Net;
using Bitwarden.Server.Sdk.Sagas;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// The middleware every service runs, seen through a whole service: what a request carries by the time its handler
/// runs, and what is refused before it gets there.
/// </summary>
/// <remarks>
/// Each piece has tests of its own. These pin that the service puts it in the pipeline, so a request through the host
/// meets it.
/// </remarks>
public sealed class PlatformMiddlewareTests
{
    private const string ReadPath = "/pipeline-read";
    private const string WritePath = "/pipeline-write";
    private const string SpanningPath = "/pipeline-spanning";
    private const string Handled = "The pipeline probe handled the request.";

    private static readonly IPAddress Proxy = IPAddress.Parse("10.0.0.5");
    private static readonly IPAddress Client = IPAddress.Parse("203.0.113.7");
    private static readonly Guid Person = new("a1000000-0000-4000-8000-0000000000b1");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // FinishedWrites: a write runs to the end once sent, so its handler sees a cancellation that never fires, whatever
    // the client does.
    [Fact]
    public async Task AWrite_WhoseClientHasLeft_ReachesItsHandlerUncancelled()
    {
        var seen = new Seen();
        await using var service = await StartAsync(seen, connection: http => http.Features.Set<IHttpRequestLifetimeFeature>(new Left()));

        using var response = await service.Client().PostAsync(WritePath, content: null, Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(seen.Aborted.CanBeCanceled);
    }

    // ForwardedClientAddress: the address a trusted proxy was connected from is the one the service sees.
    [Fact]
    public async Task ARequest_FromATrustedProxy_SeesTheAddressItForwarded()
    {
        var seen = new Seen();
        await using var service = await StartAsync(
            seen,
            connection: http => http.Connection.RemoteIpAddress = Proxy,
            configuration: new Dictionary<string, string?> { ["ForwardedHeaders:KnownProxies"] = Proxy.ToString() });
        using var request = new HttpRequestMessage(HttpMethod.Get, ReadPath);
        request.Headers.Add("X-Forwarded-For", Client.ToString());

        using var response = await service.Client().SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(Client, seen.Address);
    }

    // ContextLogScope: a line a handler logs names the person the request is about.
    [Fact]
    public async Task ALineTheHandlerLogs_CarriesWhomTheRequestIsAbout()
    {
        var seen = new Seen();
        var lines = new ScopedLines();
        await using var service = await StartAsync(seen, services: services => services.AddSingleton<ILoggerProvider>(lines));
        using var request = ProductServiceAttributeTests.Get(ReadPath, SessionHost.Token(Person, SessionHost.Scope));

        using var response = await service.Client().SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var scopes = Assert.Single(lines.Lines, line => line.Message == Handled).Scopes;
        Assert.Contains(new KeyValuePair<string, object?>("UserId", Person), scopes);
    }

    // IpRateLimiting: with a rule configured, the request over it is refused before anything answers it.
    [Fact]
    public async Task ARequest_OverAConfiguredLimit_IsRefused429()
    {
        var seen = new Seen();
        await using var service = await StartAsync(
            seen,
            connection: http => http.Connection.RemoteIpAddress = Client,
            configuration: new Dictionary<string, string?>
            {
                ["IpRateLimitOptions:GeneralRules:0:Endpoint"] = "*",
                ["IpRateLimitOptions:GeneralRules:0:Period"] = "1m",
                ["IpRateLimitOptions:GeneralRules:0:Limit"] = "1",
            });

        using var first = await service.Client().GetAsync(ReadPath, Token);
        using var second = await service.Client().GetAsync(ReadPath, Token);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(1, seen.Handled);
    }

    // OperationWrites: an operation's writes are watched, so one that spans services without declaring how is refused
    // under SpansServices:RefuseUndeclared, and never gets past its second write.
    [Fact]
    public async Task AnUndeclaredSpan_WithRefusalOn_IsRefused()
    {
        var seen = new Seen();
        await using var service = await StartAsync(
            seen,
            configuration: new Dictionary<string, string?> { ["SpansServices:RefuseUndeclared"] = "true" });

        using var response = await service.Client().PostAsync(SpanningPath, content: null, Token);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, seen.Handled);
    }

    /// <summary>
    /// A product service with the pipeline probes mapped, and the connection made to look as a test says before the
    /// platform's middleware sees it.
    /// </summary>
    private static Task<SessionHost> StartAsync(
        Seen seen,
        Action<HttpContext>? connection = null,
        IReadOnlyDictionary<string, string?>? configuration = null,
        Action<IServiceCollection>? services = null) =>
        SessionHost.StartAsync(
            productService: true,
            configuration,
            collection =>
            {
                if (connection is not null)
                {
                    collection.AddSingleton<IStartupFilter>(new Connection(connection));
                }

                services?.Invoke(collection);
            },
            endpoints => Map(endpoints, seen));

    private static void Map(IEndpointRouteBuilder endpoints, Seen seen)
    {
        endpoints.MapGet(ReadPath, (HttpContext http, ILogger<PlatformMiddlewareTests> logger) =>
        {
            seen.Address = http.Connection.RemoteIpAddress;
            seen.Handled++;
            logger.LogInformation(Handled);
            return Results.NoContent();
        }).AllowAnonymous().ExcludeFromDescription();

        endpoints.MapPost(WritePath, (HttpContext http) =>
        {
            seen.Aborted = http.RequestAborted;
            seen.Handled++;
            return Results.NoContent();
        }).AllowAnonymous().ExcludeFromDescription();

        endpoints.MapPost(SpanningPath, () =>
        {
            ServiceWrites.Saving();
            ServiceWrites.Outgoing("billing-service", Guid.NewGuid().ToString());
            seen.Handled++;
            return Results.NoContent();
        }).AllowAnonymous().ExcludeFromDescription();
    }

    /// <summary>
    /// What the probes' handlers saw.
    /// </summary>
    private sealed class Seen
    {
        public CancellationToken Aborted { get; set; } = new(canceled: true);

        public IPAddress? Address { get; set; }

        public int Handled { get; set; }
    }

    /// <summary>
    /// Changes the connection ahead of every middleware the service has, as the server would have made it.
    /// </summary>
    private sealed class Connection(Action<HttpContext> change) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((http, following) =>
            {
                change(http);
                return following(http);
            });
            next(app);
        };
    }

    /// <summary>
    /// A connection whose client has already gone.
    /// </summary>
    private sealed class Left : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; } = new(canceled: true);

        public void Abort()
        {
        }
    }

    /// <summary>
    /// Every line logged, with the fields of every scope it was written in.
    /// </summary>
    private sealed class ScopedLines : ILoggerProvider, ISupportExternalScope, ILogger
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

        public ConcurrentQueue<(string Message, List<KeyValuePair<string, object?>> Scopes)> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = new List<KeyValuePair<string, object?>>();
            _scopes.ForEachScope(
                (scope, collected) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
                    {
                        collected.AddRange(pairs.Select(pair => new KeyValuePair<string, object?>(pair.Key, pair.Value)));
                    }
                },
                fields);
            Lines.Enqueue((formatter(state, exception), fields));
        }

        public void Dispose()
        {
        }
    }
}
