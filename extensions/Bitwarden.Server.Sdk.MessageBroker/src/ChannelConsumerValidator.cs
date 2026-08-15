using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Marks that an <see cref="ISubscriber{T}"/> was registered for a topic key.</summary>
internal sealed record ChannelSubscriberDescriptor(Type MessageType, string SubscriptionKey);

/// <summary>Marks that a <see cref="MessageConsumer{T}"/> was registered for a topic key.</summary>
internal sealed record ChannelConsumerDescriptor(Type MessageType, string SubscriptionKey);

/// <summary>
/// Validates at startup that every channel subscriber has a corresponding
/// <see cref="MessageConsumer{T}"/> hosted service. Only runs when the in-memory channel
/// backend is active (no Azure Service Bus or Rabbit connection string configured).
/// </summary>
internal sealed class ChannelConsumerValidationService(
    IOptions<MessagingOptions> options,
    IEnumerable<ChannelSubscriberDescriptor> subscribers,
    IEnumerable<ChannelConsumerDescriptor> consumers) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (!string.IsNullOrEmpty(opts.AzureServiceBusConnectionString) ||
            !string.IsNullOrEmpty(opts.RabbitUri))
            return Task.CompletedTask;

        var consumerKeys = consumers
            .Select(c => (c.MessageType, c.SubscriptionKey))
            .ToHashSet();

        var missing = subscribers
            .DistinctBy(l => (l.MessageType, l.SubscriptionKey))
            .Where(l => !consumerKeys.Contains((l.MessageType, l.SubscriptionKey)))
            .ToList();

        if (missing.Count > 0)
        {
            var list = string.Join(", ", missing.Select(m => $"{m.MessageType.Name}({m.SubscriptionKey})"));
            throw new InvalidOperationException(
                $"The following channel subscribers have no MessageConsumer<T> registered: {list}. " +
                $"Use AddMessageConsumer instead of AddSubscriber for the in-memory channel backend, " +
                $"or configure a Rabbit or Azure Service Bus connection.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
