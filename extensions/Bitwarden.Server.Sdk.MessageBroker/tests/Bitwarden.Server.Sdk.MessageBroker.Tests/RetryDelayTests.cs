using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// A hosted consumer that throws dead-letters the message unless its subscription or queue has
/// retries configured (<see cref="ConsumerRetryOptions"/>); with them it waits, then returns it.
/// </summary>
public class RetryDelayTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(50, 30)]
    public void TheDelayDoublesWithEachDeliveryUpToTheMaximum(int deliveryCount, int expectedSeconds)
    {
        var options = new ConsumerRetryOptions { RetryDelay = TimeSpan.FromSeconds(1), MaxRetryDelay = TimeSpan.FromSeconds(30) };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), options.RetryDelayAfter(deliveryCount));
    }

    [Fact]
    public void ByDefaultAMessageIsNotRetried()
    {
        var options = new ConsumerRetryOptions();

        Assert.Equal(1, options.MaxDeliveries);
        Assert.Equal(TimeSpan.Zero, options.RetryDelayAfter(1));
    }

    [Theory]
    [InlineData(0, 0, 30)]
    [InlineData(2, -1, 30)]
    [InlineData(2, 10, 5)]
    public void InvalidRetrySettingsAreRefused(int maxDeliveries, int retryDelaySeconds, int maxRetryDelaySeconds)
    {
        var result = new ConsumerRetryOptionsValidator().Validate("retry/retry", new ConsumerRetryOptions
        {
            MaxDeliveries = maxDeliveries,
            RetryDelay = TimeSpan.FromSeconds(retryDelaySeconds),
            MaxRetryDelay = TimeSpan.FromSeconds(maxRetryDelaySeconds),
        });

        Assert.True(result.Failed);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task InvalidRetrySettingsStopTheHostStarting()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<DeliveryClock>();
                services.AddMessageConsumer<MyItemPayload, MyItem, FailsOnceItemConsumer>("retry", "retry", retry => retry.MaxDeliveries = 0);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task ByDefault_AMessageWhoseConsumerThrowsIsDeadLetteredOnItsFirstDelivery()
    {
        var host = await StartTopicHostAsync<AlwaysFailsItemConsumer>(configureRetry: null);
        var clock = host.Services.GetRequiredService<DeliveryClock>();

        await Publish(host, 1);
        await clock.WaitForAsync(1);
        await WaitForLogAsync(host, "was dead-lettered");
        await Task.Delay(500, TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, clock.Count);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task AMessageIsRetriedUpToItsMaxDeliveriesThenDeadLettered()
    {
        var host = await StartTopicHostAsync<AlwaysFailsItemConsumer>(retry => retry.MaxDeliveries = 3);
        var clock = host.Services.GetRequiredService<DeliveryClock>();

        await Publish(host, 1);
        await clock.WaitForAsync(3);
        await WaitForLogAsync(host, "was dead-lettered");
        await Task.Delay(500, TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, clock.Count);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task ATopicConsumerWaitsBeforeTheMessageIsRedelivered()
    {
        var host = await StartTopicHostAsync<FailsOnceItemConsumer>(retry =>
        {
            retry.MaxDeliveries = 2;
            retry.RetryDelay = TimeSpan.FromMilliseconds(500);
        });

        await Publish(host, 1);
        var deliveries = await host.Services.GetRequiredService<DeliveryClock>().WaitForAsync(2);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(deliveries[1] - deliveries[0] >= TimeSpan.FromMilliseconds(450),
            $"Redelivered after {deliveries[1] - deliveries[0]}.");
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task RetrySettingsCanComeFromConfiguration()
    {
        var host = new HostBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Retry:MaxDeliveries"] = "2",
                ["Retry:RetryDelay"] = "00:00:00.500",
            }))
            .ConfigureServices(services =>
            {
                services.AddSingleton<DeliveryClock>();
                services.AddPublisher<MyItemPayload, MyItem>("retry");
                services.AddMessageConsumer<MyItemPayload, MyItem, FailsOnceItemConsumer>("retry", "retry");
                services.AddOptions<ConsumerRetryOptions>("retry/retry").BindConfiguration("Retry");
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        await Publish(host, 1);
        var deliveries = await host.Services.GetRequiredService<DeliveryClock>().WaitForAsync(2);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(deliveries[1] - deliveries[0] >= TimeSpan.FromMilliseconds(450),
            $"Redelivered after {deliveries[1] - deliveries[0]}.");
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task ACommandConsumerWaitsBeforeTheCommandIsRedelivered()
    {
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<DeliveryClock>();
                services.AddCommandSender<SendMailPayload, SendMail>("retry.commands");
                services.AddCommandConsumer<SendMailPayload, SendMail, FailsOnceMailConsumer>("retry.commands", retry =>
                {
                    retry.MaxDeliveries = 2;
                    retry.RetryDelay = TimeSpan.FromMilliseconds(500);
                });
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        await host.Services.GetRequiredKeyedService<Publisher<SendMailPayload, SendMail>>(MessageBrokerKeys.Publisher<SendMailPayload>("retry.commands"))
            .Publish(new SendMail(1)).SendAsync(TestContext.Current.CancellationToken);

        var deliveries = await host.Services.GetRequiredService<DeliveryClock>().WaitForAsync(2);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(deliveries[1] - deliveries[0] >= TimeSpan.FromMilliseconds(450),
            $"Redelivered after {deliveries[1] - deliveries[0]}.");
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task ByDefault_ACommandWhoseConsumerThrowsIsDeadLetteredOnItsFirstDelivery()
    {
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddFakeLogging();
                services.AddSingleton<DeliveryClock>();
                services.AddCommandSender<SendMailPayload, SendMail>("default.commands");
                services.AddCommandConsumer<SendMailPayload, SendMail, AlwaysFailsMailConsumer>("default.commands");
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        await host.Services.GetRequiredKeyedService<Publisher<SendMailPayload, SendMail>>(MessageBrokerKeys.Publisher<SendMailPayload>("default.commands"))
            .Publish(new SendMail(1)).SendAsync(TestContext.Current.CancellationToken);

        var clock = host.Services.GetRequiredService<DeliveryClock>();
        await clock.WaitForAsync(1);
        await WaitForLogAsync(host, "dead-lettered");
        await Task.Delay(500, TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, clock.Count);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task AHandlerThatSettlesItsMessageBeforeThrowing_DoesNotDelayTheNextOne()
    {
        // A long delay, which the next message must not wait for: the first is dead-lettered by its
        // handler, so it will never come back and there is nothing to delay.
        var host = await StartTopicHostAsync<DeadLettersThenThrowsItemConsumer>(retry =>
        {
            retry.MaxDeliveries = 2;
            retry.RetryDelay = TimeSpan.FromSeconds(10);
        });

        await Publish(host, 1);
        await Publish(host, 2);

        var deliveries = await host.Services.GetRequiredService<DeliveryClock>().WaitForAsync(2);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(deliveries[1] - deliveries[0] < TimeSpan.FromSeconds(5),
            $"The next message waited {deliveries[1] - deliveries[0]}.");
    }

    private static async Task<IHost> StartTopicHostAsync<TConsumer>(Action<ConsumerRetryOptions>? configureRetry)
        where TConsumer : class, IMessageConsumer<MyItemPayload, MyItem>
    {
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddFakeLogging();
                services.AddSingleton<DeliveryClock>();
                services.AddPublisher<MyItemPayload, MyItem>("retry");
                services.AddMessageConsumer<MyItemPayload, MyItem, TConsumer>("retry", "retry", configureRetry);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private static Task Publish(IHost host, int id) =>
        host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(MessageBrokerKeys.Publisher<MyItemPayload>("retry"))
            .Publish(new MyItem(id)).SendAsync(TestContext.Current.CancellationToken);

    private static async Task WaitForLogAsync(IHost host, string text)
    {
        var logs = host.Services.GetFakeLogCollector();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!logs.GetSnapshot().Any(record => record.Level == LogLevel.Warning && record.Message.Contains(text)))
        {
            Assert.True(DateTime.UtcNow < deadline, $"No warning containing \"{text}\" was logged.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    public sealed class DeliveryClock
    {
        private readonly ConcurrentQueue<DateTime> _deliveries = new();

        public int Count => _deliveries.Count;

        public int Record()
        {
            _deliveries.Enqueue(DateTime.UtcNow);
            return _deliveries.Count;
        }

        public async Task<IReadOnlyList<DateTime>> WaitForAsync(int count)
        {
            while (_deliveries.Count < count)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            return [.. _deliveries];
        }
    }

    public sealed class FailsOnceItemConsumer(DeliveryClock clock) : IMessageConsumer<MyItemPayload, MyItem>
    {
        public Task HandleAsync(Envelope<MyItemPayload, MyItem> envelope, CancellationToken cancellationToken) =>
            clock.Record() == 1 ? throw new InvalidOperationException("First delivery fails on purpose.") : Task.CompletedTask;
    }

    public sealed class AlwaysFailsItemConsumer(DeliveryClock clock) : IMessageConsumer<MyItemPayload, MyItem>
    {
        public Task HandleAsync(Envelope<MyItemPayload, MyItem> envelope, CancellationToken cancellationToken)
        {
            clock.Record();
            throw new InvalidOperationException("Every delivery fails on purpose.");
        }
    }

    public sealed class DeadLettersThenThrowsItemConsumer(DeliveryClock clock) : IMessageConsumer<MyItemPayload, MyItem>
    {
        public async Task HandleAsync(Envelope<MyItemPayload, MyItem> envelope, CancellationToken cancellationToken)
        {
            clock.Record();
            if (envelope.Payload.Id == 1)
            {
                await envelope.DeadLetterAsync("Set aside on purpose.", cancellationToken);
                throw new InvalidOperationException("Throws after setting the message aside.");
            }
        }
    }

    public sealed class FailsOnceMailConsumer(DeliveryClock clock) : IMessageConsumer<SendMailPayload, SendMail>
    {
        public Task HandleAsync(Envelope<SendMailPayload, SendMail> envelope, CancellationToken cancellationToken) =>
            clock.Record() == 1 ? throw new InvalidOperationException("First delivery fails on purpose.") : Task.CompletedTask;
    }

    public sealed class AlwaysFailsMailConsumer(DeliveryClock clock) : IMessageConsumer<SendMailPayload, SendMail>
    {
        public Task HandleAsync(Envelope<SendMailPayload, SendMail> envelope, CancellationToken cancellationToken)
        {
            clock.Record();
            throw new InvalidOperationException("Every delivery fails on purpose.");
        }
    }
}
