using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using Bitwarden.Server.Sdk.Sagas;
using Bitwarden.Server.Sdk.Service.Sagas;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Service.Tests.Sagas;

/// <summary>
/// An endpoint whose writes span services: counted, logged as a warning naming the operation and where its writes
/// went and, with <c>SpansServices:RefuseUndeclared</c>, refused before the write that makes it span, unless the
/// handler method declares its shape; never for one write, or for saves alone.
/// </summary>
/// <remarks>
/// Compiled twice, as every test here is: with the request delegate generator, and without it.
/// </remarks>
public sealed class OperationWritesTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnUndeclaredSpan_WithRefusalOn_IsRefusedBeforeTheSecondWrite()
    {
        await using var host = await Host.StartAsync(refuse: true);

        var refused = await Assert.ThrowsAsync<UndeclaredSpanException>(
            () => host.Client.PostAsync("/undeclared", content: null, Token));

        Assert.Equal(1, Writes.Made("undeclared"));
        Assert.Contains("database, then billing-service", refused.Message, StringComparison.Ordinal);
        Assert.Contains("[SpansServices(SpanShape.", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Bitwarden.Server.Sdk.Sagas PACKAGE.md", refused.Message, StringComparison.Ordinal);
        Assert.Contains(host.Logs, log => log.Level == LogLevel.Warning && log.Message.Contains("Undeclared writes across services", StringComparison.Ordinal));
        Assert.Contains(host.Spans, span => span.Operation.Contains("Undeclared", StringComparison.Ordinal) && !span.Declared);
    }

    // Off (the default, and Development's): logged and counted, never refused, so a flow no test covers isn't broken.
    [Fact]
    public async Task AnUndeclaredSpan_WithRefusalOff_IsOnlyLoggedAndCounted()
    {
        await using var host = await Host.StartAsync(refuse: false);

        using var response = await host.Client.PostAsync("/undeclared-off", content: null, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, Writes.Made("undeclared-off"));
        Assert.Contains(host.Logs, log => log.Level == LogLevel.Warning && log.Message.Contains("Undeclared writes", StringComparison.Ordinal));
        Assert.Contains(host.Spans, span => !span.Declared);
    }

    [Fact]
    public async Task ADeclaredSpan_IsCountedNotRefused()
    {
        await using var host = await Host.StartAsync(refuse: true);

        using var response = await host.Client.PostAsync("/declared", content: null, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(host.Logs, log => log.Message.Contains("Undeclared writes", StringComparison.Ordinal));
        Assert.Contains(host.Spans, span => span.Declared);
    }

    // An admitted defect: never refused, but as loud as an undeclared span, and counted as unhandled.
    [Fact]
    public async Task AnAdmittedDefect_IsLoggedNotRefused()
    {
        await using var host = await Host.StartAsync(refuse: true);

        using var response = await host.Client.PostAsync("/unhandled", content: null, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(host.Logs, log => log.Level == LogLevel.Warning && log.Message.Contains("known defect", StringComparison.Ordinal));
        Assert.Contains(host.Spans, span => !span.Declared);
    }

    [Theory]
    [InlineData("/one-write")]
    [InlineData("/saves-alone")]
    public async Task NoSpan_IsNeitherRefusedNorCounted(string path)
    {
        await using var host = await Host.StartAsync(refuse: true);

        using var response = await host.Client.PostAsync(path, content: null, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(host.Spans);
    }

    // An operation that catches the refusal and goes on still fails: it can't succeed as if the write had been made.
    [Fact]
    public async Task ARefusalTheOperationCaught_FailsItWhenItReturns()
    {
        await using var host = await Host.StartAsync(refuse: true);

        await Assert.ThrowsAsync<UndeclaredSpanException>(() => host.Client.PostAsync("/caught", content: null, Token));
    }

    /// <summary>
    /// The writes each endpoint made, standing in for a service client and a database.
    /// </summary>
    private static class Writes
    {
        private static readonly ConcurrentDictionary<string, int> Counts = new();

        public static int Made(string endpoint) => Counts.GetValueOrDefault(endpoint);

        public static void Save(string endpoint)
        {
            ServiceWrites.Saving();
            Counts.AddOrUpdate(endpoint, 1, (_, count) => count + 1);
        }

        public static void Send(string endpoint, string service)
        {
            ServiceWrites.Outgoing(service, Guid.NewGuid().ToString());
            Counts.AddOrUpdate(endpoint, 1, (_, count) => count + 1);
        }
    }

    private static class Endpoints
    {
        public static IResult Undeclared()
        {
            Writes.Save("undeclared");
            Writes.Send("undeclared", "billing-service");
            return Results.Ok();
        }

        public static IResult UndeclaredOff()
        {
            Writes.Save("undeclared-off");
            Writes.Send("undeclared-off", "billing-service");
            return Results.Ok();
        }

        [SpansServices(SpanShape.Compensated, "The save is undone when the call fails (a test's own).")]
        public static IResult Declared()
        {
            Writes.Save("declared");
            Writes.Send("declared", "billing-service");
            return Results.Ok();
        }

        [SpansServicesUnhandled("A failure after the save leaves the call unmade; recorded for its owner (a test's own).")]
        public static IResult Unhandled()
        {
            Writes.Save("unhandled");
            Writes.Send("unhandled", "billing-service");
            return Results.Ok();
        }

        public static IResult OneWrite()
        {
            Writes.Send("one-write", "billing-service");
            return Results.Ok();
        }

        public static IResult SavesAlone()
        {
            Writes.Save("saves-alone");
            Writes.Save("saves-alone");
            return Results.Ok();
        }

        public static IResult Caught()
        {
            Writes.Save("caught");
            try
            {
                Writes.Send("caught", "billing-service");
            }
            catch (UndeclaredSpanException)
            {
                // Swallowed, as a careless compensation would.
            }

            return Results.Ok();
        }
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly MeterListener _listener = new();

        private Host(WebApplication app, ConcurrentQueue<(LogLevel Level, string Message)> logs)
        {
            _app = app;
            Logs = logs;
            Client = app.GetTestClient();

            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == WriteSpanMetrics.MeterName && instrument.Name == WriteSpanMetrics.SpansName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                string? operation = null;
                var declared = false;
                foreach (var tag in tags)
                {
                    if (tag.Key == WriteSpanMetrics.OperationTag)
                    {
                        operation = (string?)tag.Value;
                    }
                    else if (tag.Key == WriteSpanMetrics.DeclaredTag)
                    {
                        declared = (bool)tag.Value!;
                    }
                }

                Spans.Enqueue((operation ?? string.Empty, declared));
            });
            _listener.Start();
        }

        public HttpClient Client { get; }

        public ConcurrentQueue<(LogLevel Level, string Message)> Logs { get; }

        public ConcurrentQueue<(string Operation, bool Declared)> Spans { get; } = new();

        public static async Task<Host> StartAsync(bool refuse)
        {
            var logs = new ConcurrentQueue<(LogLevel, string)>();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders().AddProvider(new Collecting(logs));
            builder.Services.AddRouting();
            builder.Services.AddMetrics();
            builder.Services.Configure<SpansServicesOptions>(options => options.RefuseUndeclared = refuse);
            builder.Services.AddSingleton<WriteSpanMetrics>();
            builder.Services.AddSingleton<IServiceWritesGuard, SpansServicesGuard>();

            var app = builder.Build();
            app.UseRouting();
            app.UseMiddleware<OperationWrites>();
            app.MapPost("/undeclared", Endpoints.Undeclared);
            app.MapPost("/undeclared-off", Endpoints.UndeclaredOff);
            app.MapPost("/declared", Endpoints.Declared);
            app.MapPost("/unhandled", Endpoints.Unhandled);
            app.MapPost("/one-write", Endpoints.OneWrite);
            app.MapPost("/saves-alone", Endpoints.SavesAlone);
            app.MapPost("/caught", Endpoints.Caught);

            await app.StartAsync(Token);
            return new Host(app, logs);
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Dispose();
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }

    private sealed class Collecting(ConcurrentQueue<(LogLevel, string)> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(logs);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<(LogLevel, string)> logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                logs.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
