using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

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
        var options = new MessagingOptions { RetryDelay = TimeSpan.FromSeconds(1), MaxRetryDelay = TimeSpan.FromSeconds(30) };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), options.RetryDelayAfter(deliveryCount));
    }

    [Fact]
    public void AZeroDelayRedeliversAtOnce()
    {
        var options = new MessagingOptions { RetryDelay = TimeSpan.Zero };

        Assert.Equal(TimeSpan.Zero, options.RetryDelayAfter(5));
    }

    [Fact]
    public void AMaximumShorterThanTheDelayIsRefused()
    {
        var result = new MessagingOptionsValidator().Validate(null,
            new MessagingOptions { RetryDelay = TimeSpan.FromSeconds(10), MaxRetryDelay = TimeSpan.FromSeconds(5) });

        Assert.True(result.Failed);
    }

    [Fact]
    public void ALockRenewalBoundShorterThanTheMaximumRetryDelayIsRefused()
    {
        var result = new MessagingOptionsValidator().Validate(null,
            new MessagingOptions { MaxRetryDelay = TimeSpan.FromSeconds(30), MaxLockRenewalDuration = TimeSpan.FromSeconds(10) });

        Assert.True(result.Failed);
    }

    private static readonly Dictionary<string, string?> ShortRetryDelay = new() { ["RetryDelay"] = "00:00:00.500" };

    [Fact(Timeout = 60 * 1000)]
    public async Task ATopicConsumerWaitsBeforeTheMessageIsRedelivered()
    {
        var host = new HostBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(ShortRetryDelay))
            .ConfigureServices(services =>
            {
                services.AddSingleton<DeliveryClock>();
                services.AddPublisher<MyItemPayload, MyItem>("retry");
                services.AddMessageConsumer<MyItemPayload, MyItem, FailsOnceItemConsumer>("retry", "retry");
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        await host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(MessageBrokerKeys.Publisher<MyItemPayload>("retry"))
            .Publish(new MyItem(1)).SendAsync(TestContext.Current.CancellationToken);

        var deliveries = await host.Services.GetRequiredService<DeliveryClock>().WaitForAsync(2);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(deliveries[1] - deliveries[0] >= TimeSpan.FromMilliseconds(450),
            $"Redelivered after {deliveries[1] - deliveries[0]}.");
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task ACommandConsumerWaitsBeforeTheCommandIsRedelivered()
    {
        var host = new HostBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(ShortRetryDelay))
            .ConfigureServices(services =>
            {
                services.AddSingleton<DeliveryClock>();
                services.AddCommandSender<SendMailPayload, SendMail>("retry.commands");
                services.AddCommandConsumer<SendMailPayload, SendMail, FailsOnceMailConsumer>("retry.commands");
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

    public sealed class DeliveryClock
    {
        private readonly ConcurrentQueue<DateTime> _deliveries = new();

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

    public sealed class FailsOnceMailConsumer(DeliveryClock clock) : IMessageConsumer<SendMailPayload, SendMail>
    {
        public Task HandleAsync(Envelope<SendMailPayload, SendMail> envelope, CancellationToken cancellationToken) =>
            clock.Record() == 1 ? throw new InvalidOperationException("First delivery fails on purpose.") : Task.CompletedTask;
    }
}
