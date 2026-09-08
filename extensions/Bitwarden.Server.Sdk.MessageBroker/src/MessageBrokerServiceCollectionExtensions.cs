using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods for registering message broker services.</summary>
public static class MessageBrokerServiceCollectionExtensions
{
    /// <summary>Registers a keyed <see cref="Publisher{TPayload, TCeiling}"/> for the given topic name.</summary>
    public static IServiceCollection AddPublisher<TPayload, TCeiling>(this IServiceCollection services, string name)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
    {
        // DI time enforcement of valid payload variant chain.
        ChainValidator<TPayload, TCeiling>.ThrowIfInvalid();
        ClaimTopicForPayload<TPayload>(services, name);
        services.AddOptions();
        services.TryAddSingleton<MessageBrokerMetrics>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MessagingOptions>, MessagingOptionsValidator>());
        services.AddOptions<MessagingOptions>().ValidateOnStart();
        AddRabbitInfrastructure(services);

        services.TryAddKeyedSingleton<IMessageSerializer>(name, (sp, key) =>
            new SystemTextJsonMessageSerializer(
                sp.GetRequiredService<IOptionsMonitor<MessageBrokerSerializerOptions>>(), (string)key!));
        services.TryAddKeyedSingleton<ChannelTopic<TPayload, TCeiling>>(name, (sp, key) =>
            new ChannelTopic<TPayload, TCeiling>(
                sp.GetRequiredService<IOptionsMonitor<MessageTopicOptions<TPayload>>>().Get((string)key!).SubscriptionNames,
                sp.GetServices<ChannelEscrowRegistration<TPayload, TCeiling>>(),
                sp.GetRequiredService<MessageBrokerMetrics>(),
                (string)key!));
        services.TryAddKeyedSingleton<Publisher<TPayload, TCeiling>>(name, (sp, key) =>
        {
            var options = sp.GetRequiredService<IOptions<MessagingOptions>>().Value;
            var serializer = sp.GetRequiredKeyedService<IMessageSerializer>(key);
            var metrics = sp.GetRequiredService<MessageBrokerMetrics>();
            if (!string.IsNullOrEmpty(options.AzureServiceBusConnectionString))
            {
                return new AzureServiceBusPublisher<TPayload, TCeiling>(options.AzureServiceBusConnectionString, name, serializer, metrics);
            }
            if (!string.IsNullOrEmpty(options.RabbitUri))
            {
                return new RabbitPublisher<TPayload, TCeiling>(sp.GetRequiredService<RabbitConnection>(), name, serializer, metrics);
            }
            return new ChannelPublisher<TPayload, TCeiling>(sp.GetRequiredKeyedService<ChannelTopic<TPayload, TCeiling>>(key), name, metrics,
                options.MaxDeliveryCount, sp.GetRequiredService<ILogger<ChannelPublisher<TPayload, TCeiling>>>());
        });

        services.AddSingleton(new RabbitTopologyDeclaration(name));

        return services;
    }

    /// <summary>
    /// Registers a keyed <see cref="ISubscriber{TPayload, TCeiling}"/> for the given topic name.
    /// </summary>
    /// <remarks>
    /// Each unique <paramref name="subscriptionName"/> receives every message independently,
    /// while multiple instances sharing the same subscription name compete for each message
    /// (pub/sub fan-out with per-group competing consumers). The resolved service key is
    /// <c>name/subscriptionName</c>.
    /// </remarks>
    public static IServiceCollection AddSubscriber<TPayload, TCeiling>(this IServiceCollection services, string name, string subscriptionName)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
    {
        // DI time enforcement of valid payload variant chain.
        ChainValidator<TPayload, TCeiling>.ThrowIfInvalid();
        ClaimTopicForPayload<TPayload>(services, name);
        var subscriptionKey = $"{name}/{subscriptionName}";

        services.AddOptions();
        services.TryAddSingleton<MessageBrokerMetrics>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MessagingOptions>, MessagingOptionsValidator>());
        services.AddOptions<MessagingOptions>().ValidateOnStart();
        AddRabbitInfrastructure(services);

        services.TryAddKeyedSingleton<IMessageSerializer>(name, (sp, key) =>
            new SystemTextJsonMessageSerializer(
                sp.GetRequiredService<IOptionsMonitor<MessageBrokerSerializerOptions>>(), (string)key!));
        services.Configure<MessageTopicOptions<TPayload>>(name, opts => opts.SubscriptionNames.Add(subscriptionName));
        services.TryAddKeyedSingleton<ChannelTopic<TPayload, TCeiling>>(name, (sp, key) =>
            new ChannelTopic<TPayload, TCeiling>(
                sp.GetRequiredService<IOptionsMonitor<MessageTopicOptions<TPayload>>>().Get((string)key!).SubscriptionNames,
                sp.GetServices<ChannelEscrowRegistration<TPayload, TCeiling>>(),
                sp.GetRequiredService<MessageBrokerMetrics>(),
                (string)key!));
        // Register ChannelTopic as IHostedService exactly once per (TPayload, name) so it stops
        // last in the LIFO shutdown sequence — after all consumers. ChannelTopic.StopAsync drains
        // remaining channel messages to escrow before sealing writers. We use a private marker
        // type to detect duplicates independently of the keyed-singleton registration (which
        // AddPublisher may have already created for the same topic).
        var marker = new ChannelTopicLifetimeMarker(typeof(TPayload), name);
        if (!services.Any(d => d.ServiceType == typeof(ChannelTopicLifetimeMarker)
                               && marker.Equals(d.ImplementationInstance)))
        {
            services.AddSingleton(marker);
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredKeyedService<ChannelTopic<TPayload, TCeiling>>(name));
        }

        services.TryAddKeyedSingleton<ISubscriber<TPayload, TCeiling>>(subscriptionKey, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptions<MessagingOptions>>().Value;
            var serializer = sp.GetRequiredKeyedService<IMessageSerializer>(name);
            var metrics = sp.GetRequiredService<MessageBrokerMetrics>();
            if (!string.IsNullOrEmpty(options.AzureServiceBusConnectionString))
            {
                return new AzureServiceBusSubscriber<TPayload, TCeiling>(options.AzureServiceBusConnectionString, name, subscriptionName, serializer, metrics);
            }
            if (!string.IsNullOrEmpty(options.RabbitUri))
            {
                return new RabbitSubscriber<TPayload, TCeiling>(sp.GetRequiredService<RabbitConnection>(), name, subscriptionName, serializer, metrics);
            }
            return new ChannelSubscriber<TPayload, TCeiling>(sp.GetRequiredKeyedService<ChannelTopic<TPayload, TCeiling>>(name).GetOrAddSubscription(subscriptionName).Reader, name, metrics);
        });

        services.AddSingleton(new ChannelSubscriberDescriptor(typeof(TPayload), subscriptionKey));

        services.AddSingleton(new RabbitTopologyDeclaration(name, $"{name}.{subscriptionName}"));

        return services;
    }

    /// <summary>
    /// Registers a keyed <see cref="ISubscriber{TPayload, TCeiling}"/> and a <typeparamref name="TConsumer"/>
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
    /// do not take <see cref="ISubscriber{TPayload, TCeiling}"/> as a constructor parameter —
    /// <see cref="ConsumerBackgroundService{TPayload, TCeiling, TConsumer}"/> owns the subscription
    /// and invokes <see cref="IMessageConsumer{TPayload, TCeiling}.HandleAsync"/> for each delivered
    /// message.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMessageConsumer<TPayload, TCeiling, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TConsumer>(
        this IServiceCollection services,
        string name,
        string subscriptionName)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
        where TConsumer : class, IMessageConsumer<TPayload, TCeiling>
    {
        var subscriptionKey = $"{name}/{subscriptionName}";

        services.AddSubscriber<TPayload, TCeiling>(name, subscriptionName);

        // Register ChannelEscrowRegistration so ChannelTopic resolves it via
        // IEnumerable<ChannelEscrowRegistration<T>> and wires up startup recovery and shutdown
        // drain callbacks. Keyed by subscriptionKey for deduplication; also registered unkeyed
        // so DI collects all instances when resolving the enumerable.
        var escrowIsNew = !services.Any(d => d.ServiceType == typeof(ChannelEscrowRegistration<TPayload, TCeiling>) && (string?)d.ServiceKey == subscriptionKey);
        services.TryAddKeyedSingleton<ChannelEscrowRegistration<TPayload, TCeiling>>(subscriptionKey, (sp, _) =>
            new ChannelEscrowRegistration<TPayload, TCeiling>(
                name, subscriptionName, subscriptionKey,
                sp.GetRequiredKeyedService<IMessageSerializer>(name),
                sp.GetRequiredService<IOptions<MessagingOptions>>(),
                sp.GetService<IMessageEscrowStore>(),
                sp.GetRequiredService<ILogger<ChannelEscrowRegistration<TPayload, TCeiling>>>()));
        if (escrowIsNew)
            services.AddSingleton<ChannelEscrowRegistration<TPayload, TCeiling>>(sp =>
                sp.GetRequiredKeyedService<ChannelEscrowRegistration<TPayload, TCeiling>>(subscriptionKey));

        services.TryAddSingleton<TConsumer>();

        // Register one ConsumerBackgroundService per (TConsumer, subscriptionKey) pair.
        // TryAddKeyedSingleton deduplicates by (ServiceType, ServiceKey), so the same
        // (TConsumer, subscriptionKey) pair is idempotent, while the same consumer class on a
        // different subscription gets its own keyed instance and its own IHostedService forward.
        var consumerIsNew = !services.Any(d =>
            d.ServiceType == typeof(ConsumerBackgroundService<TPayload, TCeiling, TConsumer>) &&
            (string?)d.ServiceKey == subscriptionKey);
        services.TryAddKeyedSingleton<ConsumerBackgroundService<TPayload, TCeiling, TConsumer>>(subscriptionKey, (sp, _) =>
            new ConsumerBackgroundService<TPayload, TCeiling, TConsumer>(
                sp.GetRequiredService<TConsumer>(),
                sp.GetRequiredKeyedService<ISubscriber<TPayload, TCeiling>>(subscriptionKey)));
        if (consumerIsNew)
            services.AddSingleton<IHostedService>(sp =>
                sp.GetRequiredKeyedService<ConsumerBackgroundService<TPayload, TCeiling, TConsumer>>(subscriptionKey));

        services.AddSingleton(new ChannelConsumerDescriptor(typeof(TPayload), subscriptionKey));
        // The validator is registered here (not in AddSubscriber) so it only activates when the app
        // has opted into the MessageConsumer framework. Raw AddSubscriber usage (tests, out-of-process
        // consumers on ASB/Rabbit) is not subject to this enforcement.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ChannelConsumerValidationService>());
        return services;
    }

    // Sentinel registered in DI to prevent duplicate IHostedService entries for the same ChannelTopic.
    private sealed record ChannelTopicLifetimeMarker(Type MessageType, string TopicName);

    // Sentinel binding a topic name to the payload family it carries. The wire format has no
    // payload-family discriminator, so registering two distinct payload families against the same
    // topic name on Azure Service Bus or RabbitMQ produces silent misroutes: each subscriber
    // decodes every message against its own TPayload regardless of which family the publisher sent.
    // Enforced uniformly across backends so the invariant does not depend on the runtime transport
    // configured by MessagingOptions.
    private sealed record TopicPayloadClaim(string TopicName, Type PayloadType);

    private static void ClaimTopicForPayload<TPayload>(IServiceCollection services, string name)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType != typeof(TopicPayloadClaim)) continue;
            if (descriptor.ImplementationInstance is not TopicPayloadClaim claim) continue;
            if (claim.TopicName != name) continue;
            if (claim.PayloadType == typeof(TPayload)) return;
            throw new InvalidOperationException(
                $"Topic '{name}' is already registered for payload family '{claim.PayloadType.Name}'. " +
                $"Cannot also register it for '{typeof(TPayload).Name}'. Each topic must map to a " +
                "single payload family — the wire format carries no payload discriminator, so mixing " +
                "families on one topic causes silent misroutes on external brokers.");
        }
        services.AddSingleton(new TopicPayloadClaim(name, typeof(TPayload)));
    }

    private static void AddRabbitInfrastructure(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(RabbitConnection)))
            return;
        services.AddSingleton<RabbitConnection>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<RabbitConnection>());
    }
}
