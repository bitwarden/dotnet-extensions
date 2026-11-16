using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class ResubscribeLoopTests
{
    private readonly FakeLogger _logger = new();
    private readonly FakeTimeProvider _time = new();

    // Each failed attempt is logged with the wait before the next; the waits so far, in order.
    private List<TimeSpan> Waits() =>
        [.. _logger.Collector.GetSnapshot()
            .Where(record => record.Level == LogLevel.Warning)
            .Select(record => TimeSpan.Parse(record.StructuredState!.Single(pair => pair.Key == "Delay").Value!))];

    private async Task WaitForWaitsAsync(int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (Waits().Count < count)
        {
            Assert.True(DateTime.UtcNow < deadline, $"Waited for {count} failed attempts; saw {Waits().Count}.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    // The tests here are about the wait between attempts, not readiness, so the subscribed signal is ignored.
    private Task RunAsync(Func<Action, CancellationToken, Task> receiveAsync, CancellationToken stoppingToken) =>
        ResubscribeLoop.RunAsync(
            (_, onMessage, token) => receiveAsync(onMessage, token), _logger, "topic/subscription",
            new SubscriptionReadiness(), _time, stoppingToken);

    [Fact]
    public async Task EachFailureInARowDoublesTheWait_UpToThirtySeconds()
    {
        using var stopping = new CancellationTokenSource();
        var loop = RunAsync((_, _) => throw new InvalidOperationException("broker gone"), stopping.Token);

        var expected = new[] { 1, 2, 4, 8, 16, 30, 30 }.Select(seconds => TimeSpan.FromSeconds(seconds)).ToArray();
        for (var i = 0; i < expected.Length; i++)
        {
            await WaitForWaitsAsync(i + 1);
            _time.Advance(expected[i]);
        }

        Assert.Equal(expected, Waits().Take(expected.Length));
        await stopping.CancelAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AMessageResetsTheWait()
    {
        using var stopping = new CancellationTokenSource();
        var attempts = 0;
        var loop = RunAsync((onMessage, _) =>
        {
            // The third attempt receives a message before it fails: a new outage, not the same one.
            if (++attempts == 3)
                onMessage();
            throw new InvalidOperationException("broker gone");
        }, stopping.Token);

        for (var i = 1; i <= 3; i++)
        {
            await WaitForWaitsAsync(i);
            _time.Advance(Waits()[i - 1]);
        }

        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)], Waits().Take(3));
        await stopping.CancelAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AStreamThatEndsWithoutFailing_EndsTheLoop()
    {
        var attempts = 0;
        await RunAsync((_, _) => { attempts++; return Task.CompletedTask; }, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(1, attempts);
        Assert.Empty(Waits());
    }

    [Fact]
    public async Task StoppingDuringTheWait_EndsTheLoop()
    {
        using var stopping = new CancellationTokenSource();
        var loop = RunAsync((_, _) => throw new InvalidOperationException("broker gone"), stopping.Token);
        await WaitForWaitsAsync(1);

        await stopping.CancelAsync();

        await loop.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Single(Waits());
    }
}
