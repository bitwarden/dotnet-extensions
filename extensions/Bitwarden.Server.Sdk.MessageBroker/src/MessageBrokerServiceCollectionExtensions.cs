using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods for registering message broker services.</summary>
public static class MessageBrokerServiceCollectionExtensions
{
    /// <summary>Registers a keyed <see cref="IPublisher{T}"/> for the given topic name.</summary>
    public static IServiceCollection AddPublisher<T>(this IServiceCollection services, string name)
    {
        services.AddOptions();
        services.TryAddSingleton<MessageBrokerMetrics>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MessagingOptions>, MessagingOptionsValidator>());
        services.AddOptions<MessagingOptions>().ValidateOnStart();
        AddRabbitInfrastructure(services);

        services.TryAddKeyedSingleton<IMessageSerializer>(name, (sp, key) =>
            new SystemTextJsonMessageSerializer(
                sp.GetRequiredService<IOptionsMonitor<MessageBrokerSerializerOptions>>(), (string)key!));
        services.TryAddKeyedSingleton<ChannelTopic<T>>(name, (sp, key) =>
            new ChannelTopic<T>(
                sp.GetRequiredService<IOptionsMonitor<MessageTopicOptions<T>>>().Get((string)key!).SubscriptionNames,
                sp.GetServices<ChannelEscrowRegistration<T>>(),
                sp.GetRequiredService<MessageBrokerMetrics>(),
                (string)key!));
        services.TryAddKeyedSingleton<IPublisher<T>>(name, (sp, key) =>
        {
            var options = sp.GetRequiredService<IOptions<MessagingOptions>>().Value;
            var serializer = sp.GetRequiredKeyedService<IMessageSerializer>(key);
            var metrics = sp.GetRequiredService<MessageBrokerMetrics>();
            if (!string.IsNullOrEmpty(options.AzureServiceBusConnectionString))
            {
                return new AzureServiceBusPublisher<T>(options.AzureServiceBusConnectionString, name, serializer, metrics);
            }
            if (!string.IsNullOrEmpty(options.RabbitUri))
            {
                return new RabbitPublisher<T>(sp.GetRequiredService<RabbitConnection>(), name, serializer, metrics);
            }
            return new ChannelPublisher<T>(sp.GetRequiredKeyedService<ChannelTopic<T>>(key), name, metrics,
                options.MaxDeliveryCount, sp.GetRequiredService<ILogger<ChannelPublisher<T>>>());
        });

        // Register an exchange declaration so the hosted service creates it before traffic starts.
        services.AddSingleton(new RabbitTopologyDeclaration(name));

        return services;
    }

    /// <summary>
    /// Registers a keyed <see cref="ISubscriber{T}"/> for the given topic name.
    /// </summary>
    /// <remarks>
    /// Each unique <paramref name="subscriptionName"/> receives every message independently,
    /// while multiple instances sharing the same subscription name compete for each message
    /// (pub/sub fan-out with per-group competing consumers). The resolved service key is
    /// <c>name/subscriptionName</c>.
    /// </remarks>
    public static IServiceCollection AddSubscriber<T>(this IServiceCollection services, string name, string subscriptionName)
    {
        var subscriptionKey = $"{name}/{subscriptionName}";

        services.AddOptions();
        services.TryAddSingleton<MessageBrokerMetrics>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MessagingOptions>, MessagingOptionsValidator>());
        services.AddOptions<MessagingOptions>().ValidateOnStart();
        AddRabbitInfrastructure(services);

        services.TryAddKeyedSingleton<IMessageSerializer>(name, (sp, key) =>
            new SystemTextJsonMessageSerializer(
                sp.GetRequiredService<IOptionsMonitor<MessageBrokerSerializerOptions>>(), (string)key!));
        services.Configure<MessageTopicOptions<T>>(name, opts => opts.SubscriptionNames.Add(subscriptionName));
        services.TryAddKeyedSingleton<ChannelTopic<T>>(name, (sp, key) =>
            new ChannelTopic<T>(
                sp.GetRequiredService<IOptionsMonitor<MessageTopicOptions<T>>>().Get((string)key!).SubscriptionNames,
                sp.GetServices<ChannelEscrowRegistration<T>>(),
                sp.GetRequiredService<MessageBrokerMetrics>(),
                (string)key!));
        // Register ChannelTopic<T> as IHostedService exactly once per (T, name) so it stops last
        // in the LIFO shutdown sequence — after all consumers. ChannelTopic.StopAsync drains any
        // remaining channel messages to escrow before sealing the writers. We use a private marker
        // type to detect duplicates independently of the keyed-singleton registration (which
        // AddPublisher may have already created for the same topic).
        var marker = new ChannelTopicLifetimeMarker(typeof(T), name);
        if (!services.Any(d => d.ServiceType == typeof(ChannelTopicLifetimeMarker)
                               && marker.Equals(d.ImplementationInstance)))
        {
            services.AddSingleton(marker);
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredKeyedService<ChannelTopic<T>>(name));
        }

        services.TryAddKeyedSingleton<ISubscriber<T>>(subscriptionKey, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptions<MessagingOptions>>().Value;
            var serializer = sp.GetRequiredKeyedService<IMessageSerializer>(name);
            var metrics = sp.GetRequiredService<MessageBrokerMetrics>();
            if (!string.IsNullOrEmpty(options.AzureServiceBusConnectionString))
            {
                return new AzureServiceBusSubscriber<T>(options.AzureServiceBusConnectionString, name, subscriptionName, serializer, metrics);
            }
            if (!string.IsNullOrEmpty(options.RabbitUri))
            {
                return new RabbitSubscriber<T>(sp.GetRequiredService<RabbitConnection>(), name, subscriptionName, serializer, metrics);
            }
            return new ChannelSubscriber<T>(sp.GetRequiredKeyedService<ChannelTopic<T>>(name).GetOrAddSubscription(subscriptionName).Reader, name, metrics);
        });

        // Track the subscriber so the startup validator (registered by AddMessageConsumer) can detect
        // channel subscribers without a corresponding consumer.
        services.AddSingleton(new ChannelSubscriberDescriptor(typeof(T), subscriptionKey));

        // Register an exchange+queue declaration so the hosted service creates the topology before traffic starts.
        services.AddSingleton(new RabbitTopologyDeclaration(name, $"{name}.{subscriptionName}"));

        return services;
    }

    /// <summary>
    /// Registers a keyed <see cref="ISubscriber{T}"/> and a <typeparamref name="TConsumer"/>
    /// hosted service that consumes messages from the given topic in the same process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the preferred registration for the in-memory channel backend, where the consumer
    /// must run in the same host as the publisher. It is equally valid for Azure Service Bus and
    /// Rabbit when the consumer is co-located.
    /// </para>
    /// <para>
    /// <typeparamref name="TConsumer"/> is registered as a singleton so it can be resolved by
    /// type (e.g., in tests). All constructor parameters are resolved from the service provider;
    /// do not take <see cref="ISubscriber{T}"/> as a constructor parameter —
    /// <see cref="ConsumerBackgroundService{T,TConsumer}"/> owns the subscription and invokes
    /// <see cref="IMessageConsumer{T}.HandleAsync"/> for each delivered message.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMessageConsumer<T, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TConsumer>(
        this IServiceCollection services,
        string name,
        string subscriptionName)
        where TConsumer : class, IMessageConsumer<T>
    {
        var subscriptionKey = $"{name}/{subscriptionName}";

        services.AddSubscriber<T>(name, subscriptionName);

        // Register ChannelEscrowRegistration so ChannelTopic resolves it via
        // IEnumerable<ChannelEscrowRegistration<T>> and wires up startup recovery and shutdown
        // drain callbacks. Keyed by subscriptionKey for deduplication; also registered unkeyed
        // so DI collects all instances when resolving the enumerable.
        var escrowIsNew = !services.Any(d => d.ServiceType == typeof(ChannelEscrowRegistration<T>) && (string?)d.ServiceKey == subscriptionKey);
        services.TryAddKeyedSingleton<ChannelEscrowRegistration<T>>(subscriptionKey, (sp, _) =>
            new ChannelEscrowRegistration<T>(
                name, subscriptionName, subscriptionKey,
                sp.GetRequiredKeyedService<IMessageSerializer>(name),
                sp.GetRequiredService<IOptions<MessagingOptions>>(),
                sp.GetService<IMessageEscrowStore>(),
                sp.GetRequiredService<ILogger<ChannelEscrowRegistration<T>>>()));
        if (escrowIsNew)
            services.AddSingleton<ChannelEscrowRegistration<T>>(sp =>
                sp.GetRequiredKeyedService<ChannelEscrowRegistration<T>>(subscriptionKey));

        // TConsumer is a singleton so it can be resolved by type in tests and shared across
        // subscriptions when the same consumer class handles multiple topics.
        services.TryAddSingleton<TConsumer>();

        // Register one ConsumerBackgroundService per (TConsumer, subscriptionKey) pair.
        // TryAddKeyedSingleton deduplicates by (ServiceType, ServiceKey), so the same
        // (TConsumer, subscriptionKey) pair is idempotent, while the same consumer class on a
        // different subscription gets its own keyed instance and its own IHostedService forward.
        var consumerIsNew = !services.Any(d =>
            d.ServiceType == typeof(ConsumerBackgroundService<T, TConsumer>) &&
            (string?)d.ServiceKey == subscriptionKey);
        services.TryAddKeyedSingleton<ConsumerBackgroundService<T, TConsumer>>(subscriptionKey, (sp, _) =>
            new ConsumerBackgroundService<T, TConsumer>(
                sp.GetRequiredService<TConsumer>(),
                sp.GetRequiredKeyedService<ISubscriber<T>>(subscriptionKey)));
        if (consumerIsNew)
            services.AddSingleton<IHostedService>(sp =>
                sp.GetRequiredKeyedService<ConsumerBackgroundService<T, TConsumer>>(subscriptionKey));

        services.AddSingleton(new ChannelConsumerDescriptor(typeof(T), subscriptionKey));
        // The validator is registered here (not in AddSubscriber) so it only activates when the app
        // has opted into the MessageConsumer framework. Raw AddSubscriber usage (tests, out-of-process
        // consumers on ASB/Rabbit) is not subject to this enforcement.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ChannelConsumerValidationService>());
        return services;
    }

    // Sentinel registered in DI to prevent duplicate IHostedService entries for the same ChannelTopic.
    private sealed record ChannelTopicLifetimeMarker(Type MessageType, string TopicName);

    private static void AddRabbitInfrastructure(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(RabbitConnection)))
            return;
        services.AddSingleton<RabbitConnection>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<RabbitConnection>());
    }
}
