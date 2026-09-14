using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods for registering message broker services.</summary>
public static class MessageBrokerServiceCollectionExtensions
{
    /// <summary>
    /// Registers a keyed <see cref="Publisher{TPayload, TCeiling}"/> for the given topic name.
    /// The service key is the composite <c>"{topic}#{WireName}"</c> — use
    /// <see cref="MessageBrokerKeys.Publisher{TPayload}(string)"/> to construct it, or spell it
    /// literally at a <see cref="FromKeyedServicesAttribute"/> injection site.
    /// </summary>
    public static IServiceCollection AddPublisher<TPayload, TCeiling>(this IServiceCollection services, string name)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
    {
        // DI time enforcement of valid payload variant chain.
        ChainValidator<TPayload, TCeiling>.ThrowIfInvalid();
        ClaimTopicFamily<TPayload>(services, name);
        services.AddOptions();
        services.TryAddSingleton<MessageBrokerMetrics>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MessagingOptions>, MessagingOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MessagingOptions>, PublisherCacheValidator>());
        services.AddOptions<MessagingOptions>().ValidateOnStart();
        AddRabbitInfrastructure(services);
        AddAzureServiceBusInfrastructure(services);

        var publisherKey = MessageBrokerKeys.Publisher<TPayload>(name);
        // MessageBrokerSerializerOptions is user-facing and stays scoped by topic name — one JSON
        // pipeline per topic, shared across every family that publishes on it.
        services.TryAddKeyedSingleton<IMessageSerializer>(publisherKey, (sp, _) =>
            new SystemTextJsonMessageSerializer(
                sp.GetRequiredService<IOptionsMonitor<MessageBrokerSerializerOptions>>(), name));
        services.TryAddKeyedSingleton<ChannelTopic<TPayload, TCeiling>>(publisherKey, (sp, _) =>
            new ChannelTopic<TPayload, TCeiling>(
                sp.GetRequiredService<IOptionsMonitor<MessageTopicOptions<TPayload>>>().Get(name).SubscriptionNames,
                sp.GetServices<ChannelEscrowRegistration<TPayload, TCeiling>>(),
                sp.GetRequiredService<MessageBrokerMetrics>(),
                name));
        services.TryAddKeyedSingleton<Publisher<TPayload, TCeiling>>(publisherKey, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptions<MessagingOptions>>().Value;
            var serializer = sp.GetRequiredKeyedService<IMessageSerializer>(publisherKey);
            var metrics = sp.GetRequiredService<MessageBrokerMetrics>();
            if (!string.IsNullOrEmpty(options.AzureServiceBusConnectionString))
            {
                var cache = sp.GetRequiredKeyedService<IFusionCache>(publisherKey);
                return new AzureServiceBusPublisher<TPayload, TCeiling>(sp.GetRequiredService<AzureServiceBusConnection>().Client, name, serializer, metrics, cache);
            }
            if (!string.IsNullOrEmpty(options.RabbitUri))
            {
                var cache = sp.GetRequiredKeyedService<IFusionCache>(publisherKey);
                return new RabbitPublisher<TPayload, TCeiling>(sp.GetRequiredService<RabbitConnection>(), name, serializer, metrics, cache);
            }
            return new ChannelPublisher<TPayload, TCeiling>(sp.GetRequiredKeyedService<ChannelTopic<TPayload, TCeiling>>(publisherKey), name, metrics,
                options.MaxDeliveryCount, sp.GetRequiredService<ILogger<ChannelPublisher<TPayload, TCeiling>>>());
        });

        services.AddSingleton(new BrokerTopologyDeclaration(name, TPayload.WireName));

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
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
    {
        // DI time enforcement of valid payload variant chain.
        ChainValidator<TPayload, TCeiling>.ThrowIfInvalid();
        ClaimTopicFamily<TPayload>(services, name);
        ClaimTopicSubscription<TPayload>(services, name, subscriptionName);
        var subscriptionKey = $"{name}/{subscriptionName}";
        var publisherKey = MessageBrokerKeys.Publisher<TPayload>(name);

        services.AddOptions();
        services.TryAddSingleton<MessageBrokerMetrics>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MessagingOptions>, MessagingOptionsValidator>());
        services.AddOptions<MessagingOptions>().ValidateOnStart();
        AddRabbitInfrastructure(services);
        AddAzureServiceBusInfrastructure(services);

        // Keyed on the composite publisher key so a publisher and subscriber of the same
        // (topic, family) share one serializer, while a second family on the same topic gets
        // its own. User-facing MessageBrokerSerializerOptions stays scoped by topic name.
        services.TryAddKeyedSingleton<IMessageSerializer>(publisherKey, (sp, _) =>
            new SystemTextJsonMessageSerializer(
                sp.GetRequiredService<IOptionsMonitor<MessageBrokerSerializerOptions>>(), name));
        services.Configure<MessageTopicOptions<TPayload>>(name, opts => opts.SubscriptionNames.Add(subscriptionName));
        services.TryAddKeyedSingleton<ChannelTopic<TPayload, TCeiling>>(publisherKey, (sp, _) =>
            new ChannelTopic<TPayload, TCeiling>(
                sp.GetRequiredService<IOptionsMonitor<MessageTopicOptions<TPayload>>>().Get(name).SubscriptionNames,
                sp.GetServices<ChannelEscrowRegistration<TPayload, TCeiling>>(),
                sp.GetRequiredService<MessageBrokerMetrics>(),
                name));
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
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredKeyedService<ChannelTopic<TPayload, TCeiling>>(publisherKey));
        }

        services.TryAddKeyedSingleton<ISubscriber<TPayload, TCeiling>>(subscriptionKey, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptions<MessagingOptions>>().Value;
            var serializer = sp.GetRequiredKeyedService<IMessageSerializer>(publisherKey);
            var metrics = sp.GetRequiredService<MessageBrokerMetrics>();
            if (!string.IsNullOrEmpty(options.AzureServiceBusConnectionString))
            {
                return new AzureServiceBusSubscriber<TPayload, TCeiling>(sp.GetRequiredService<AzureServiceBusConnection>().Client, name, subscriptionName, serializer, metrics);
            }
            if (!string.IsNullOrEmpty(options.RabbitUri))
            {
                return new RabbitSubscriber<TPayload, TCeiling>(sp.GetRequiredService<RabbitConnection>(), name, subscriptionName, serializer, metrics);
            }
            return new ChannelSubscriber<TPayload, TCeiling>(sp.GetRequiredKeyedService<ChannelTopic<TPayload, TCeiling>>(publisherKey).GetOrAddSubscription(subscriptionName).Reader, name, metrics);
        });

        services.AddSingleton(new ChannelSubscriberDescriptor(typeof(TPayload), subscriptionKey));

        services.AddSingleton(new BrokerTopologyDeclaration(name, TPayload.WireName, subscriptionName));

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
    /// <typeparamref name="TConsumer"/> is registered as scoped and resolved from a fresh
    /// <see cref="IServiceScope"/> per message, so handlers can inject scoped dependencies
    /// (e.g. a <c>DbContext</c>) the same way a controller action would.
    /// Tests that need to resolve the consumer by type must create a scope first
    /// (<c>using var scope = host.Services.CreateScope();</c>); apps that genuinely need a
    /// singleton consumer can pre-register <typeparamref name="TConsumer"/> themselves before
    /// calling this method. All constructor parameters are resolved from the service provider;
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
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
        where TConsumer : class, IMessageConsumer<TPayload, TCeiling>
    {
        var subscriptionKey = $"{name}/{subscriptionName}";
        var publisherKey = MessageBrokerKeys.Publisher<TPayload>(name);

        services.AddSubscriber<TPayload, TCeiling>(name, subscriptionName);

        // Register ChannelEscrowRegistration so ChannelTopic resolves it via
        // IEnumerable<ChannelEscrowRegistration<T>> and wires up startup recovery and shutdown
        // drain callbacks. Keyed by subscriptionKey for deduplication; also registered unkeyed
        // so DI collects all instances when resolving the enumerable.
        var escrowIsNew = !services.Any(d => d.ServiceType == typeof(ChannelEscrowRegistration<TPayload, TCeiling>) && (string?)d.ServiceKey == subscriptionKey);
        services.TryAddKeyedSingleton<ChannelEscrowRegistration<TPayload, TCeiling>>(subscriptionKey, (sp, _) =>
            new ChannelEscrowRegistration<TPayload, TCeiling>(
                name, subscriptionName, subscriptionKey,
                sp.GetRequiredKeyedService<IMessageSerializer>(publisherKey),
                sp.GetRequiredService<IOptions<MessagingOptions>>(),
                sp.GetService<IMessageEscrowStore>(),
                sp.GetRequiredService<ILogger<ChannelEscrowRegistration<TPayload, TCeiling>>>()));
        if (escrowIsNew)
            services.AddSingleton<ChannelEscrowRegistration<TPayload, TCeiling>>(sp =>
                sp.GetRequiredKeyedService<ChannelEscrowRegistration<TPayload, TCeiling>>(subscriptionKey));

        // TConsumer is scoped so each message is handled in its own DI scope, letting the handler
        // inject scoped dependencies (e.g. a DbContext) the same way a controller action would.
        // TryAddScoped respects a prior registration, so a consumer app that genuinely needs a
        // singleton can pre-register TConsumer themselves.
        services.TryAddScoped<TConsumer>();

        // Register one ConsumerBackgroundService per (TConsumer, subscriptionKey) pair.
        // TryAddKeyedSingleton deduplicates by (ServiceType, ServiceKey), so the same
        // (TConsumer, subscriptionKey) pair is idempotent, while the same consumer class on a
        // different subscription gets its own keyed instance and its own IHostedService forward.
        var consumerIsNew = !services.Any(d =>
            d.ServiceType == typeof(ConsumerBackgroundService<TPayload, TCeiling, TConsumer>) &&
            (string?)d.ServiceKey == subscriptionKey);
        services.TryAddKeyedSingleton<ConsumerBackgroundService<TPayload, TCeiling, TConsumer>>(subscriptionKey, (sp, _) =>
            new ConsumerBackgroundService<TPayload, TCeiling, TConsumer>(
                sp.GetRequiredService<IServiceScopeFactory>(),
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

    // Sentinel binding a (topic, family wire name) pair to the CLR payload type that owns it.
    // A topic can carry multiple families, but each family's wire name must identify exactly one
    // payload shape — two publishers stamping the same routing key on different payloads would
    // silently misroute at the subscriber.
    private sealed record TopicFamilyClaim(string TopicName, string FamilyWireName, Type PayloadType);

    // Sentinel binding a (topic, subscription) pair to the CLR payload type that reads it. Each
    // subscription is operationally single-family (the broker binding or filter narrows it to
    // one wire name), so registering a second TPayload against the same subscription would
    // deliver one broker queue into two incompatible decoders.
    private sealed record TopicSubscriptionClaim(string TopicName, string SubscriptionName, Type PayloadType);

    private static void ClaimTopicFamily<TPayload>(IServiceCollection services, string name)
        where TPayload : IPayloadWireName<TPayload>
    {
        var wireName = TPayload.WireName;
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType != typeof(TopicFamilyClaim)) continue;
            if (descriptor.ImplementationInstance is not TopicFamilyClaim claim) continue;
            if (claim.TopicName != name || claim.FamilyWireName != wireName) continue;
            if (claim.PayloadType == typeof(TPayload)) return;
            throw new InvalidOperationException(
                $"Topic '{name}' already has family wire name '{wireName}' claimed by payload " +
                $"'{claim.PayloadType.Name}'. Cannot also claim it for '{typeof(TPayload).Name}'. " +
                "Each family wire name must identify exactly one payload shape within a topic.");
        }
        services.AddSingleton(new TopicFamilyClaim(name, wireName, typeof(TPayload)));
    }

    private static void ClaimTopicSubscription<TPayload>(IServiceCollection services, string name, string subscriptionName)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType != typeof(TopicSubscriptionClaim)) continue;
            if (descriptor.ImplementationInstance is not TopicSubscriptionClaim claim) continue;
            if (claim.TopicName != name || claim.SubscriptionName != subscriptionName) continue;
            if (claim.PayloadType == typeof(TPayload)) return;
            throw new InvalidOperationException(
                $"Subscription '{subscriptionName}' on topic '{name}' is already registered for " +
                $"payload '{claim.PayloadType.Name}'. Cannot also register it for " +
                $"'{typeof(TPayload).Name}'. Each subscription receives exactly one payload family " +
                "— the broker binding picks which family reaches it.");
        }
        services.AddSingleton(new TopicSubscriptionClaim(name, subscriptionName, typeof(TPayload)));
    }

    private static void AddRabbitInfrastructure(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(RabbitConnection)))
            return;
        services.AddSingleton<RabbitConnection>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<RabbitConnection>());
    }

    private static void AddAzureServiceBusInfrastructure(IServiceCollection services)
    {
        services.TryAddSingleton<AzureServiceBusConnection>();
    }
}
