using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.MessageBroker.Microbenchmarks;

public record BenchmarkMessage(int Id);

/// <summary>
/// Measures end-to-end throughput: publish <see cref="MessageCount"/> messages and wait
/// until all are received and settled by a subscriber.
/// </summary>
[SimpleJob(warmupCount: 1, iterationCount: 5)]
public abstract class MessageBrokerBenchmarks
{
    private IHost? _host;
    private IPublisher<BenchmarkMessage>? _publisher;
    private ISubscriber<BenchmarkMessage>? _subscriber;
    private bool _configured;

    private const string TopicName = "bench";
    private const string SubscriptionKey = $"{TopicName}/{TopicName}";

    [Params(100, 1_000, 10_000)]
    public int MessageCount { get; set; }

    /// <summary>
    /// Returns the configuration for the messaging backend, or <c>null</c> if the backend
    /// is not available in the current environment (e.g., no broker running). When null is
    /// returned, <see cref="PublishAndConsumeRoundTrip"/> is a no-op for this run.
    /// </summary>
    protected abstract Dictionary<string, string?>? CreateConfig();

    /// <summary>
    /// Override to start any required infrastructure (e.g., containers) before <see cref="Setup"/> runs.
    /// </summary>
    protected virtual Task StartInfrastructureAsync() => Task.CompletedTask;

    /// <summary>
    /// Override to stop infrastructure started in <see cref="StartInfrastructureAsync"/>.
    /// </summary>
    protected virtual Task StopInfrastructureAsync() => Task.CompletedTask;

    [GlobalSetup]
    public async Task Setup()
    {
        await StartInfrastructureAsync();
        var config = CreateConfig();
        if (config is null)
        {
            _configured = false;
            return;
        }

        _host = new HostBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(config))
            .ConfigureServices(services =>
            {
                services.AddPublisher<BenchmarkMessage>(TopicName);
                services.AddSubscriber<BenchmarkMessage>(TopicName, TopicName);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await _host.StartAsync();
        _publisher = _host.Services.GetRequiredKeyedService<IPublisher<BenchmarkMessage>>(TopicName);
        _subscriber = _host.Services.GetRequiredKeyedService<ISubscriber<BenchmarkMessage>>(SubscriptionKey);
        _configured = true;
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
            _host = null;
            _publisher = null;
            _subscriber = null;
        }
        await StopInfrastructureAsync();
    }

    [Benchmark]
    public async Task PublishAndConsumeRoundTrip()
    {
        if (!_configured)
            return;

        var received = 0;
        using var cts = new CancellationTokenSource();

        var consumeTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var envelope in _subscriber!.SubscribeAsync(cts.Token))
                {
                    await envelope.CompleteAsync();
                    if (Interlocked.Increment(ref received) >= MessageCount)
                        cts.Cancel();
                }
            }
            catch (OperationCanceledException) { }
        });

        for (var i = 0; i < MessageCount; i++)
            await _publisher!.PublishAsync(new BenchmarkMessage(i), CancellationToken.None);

        await consumeTask;
    }
}
