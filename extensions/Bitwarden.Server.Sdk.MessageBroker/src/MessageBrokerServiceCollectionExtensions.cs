using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods for registering message broker services.</summary>
public static class MessageBrokerServiceCollectionExtensions
{
    /// <summary>
    /// Registers a keyed <see cref="Publisher{TFamily, TCeiling}"/> for the given topic name.
    /// The service key is the composite <c>"{topic}#{WireName}"</c> — use
    /// <see cref="MessageBrokerKeys.Publisher{TFamily}(string)"/> to construct it, or spell it
    /// literally at a <see cref="FromKeyedServicesAttribute"/> injection site.
    /// </summary>
    public static IServiceCollection AddPublisher<TFamily, TCeiling>(this IServiceCollection services, string name)
        where TFamily : FamilyCeiling<TFamily, TCeiling>, IFamilyVariants<TFamily>, IFamilyWireName<TFamily>
        where TCeiling : Family<TFamily>.ICeiling
    {
        // DI time enforcement of valid family variant chain.
        ChainValidator<TFamily, TCeiling>.ThrowIfInvalid();
        ClaimTopicFamily<TFamily>(services, name);
        services.AddOptions();
        services.TryAddSingleton<MessageBrokerMetrics>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MessagingOptions>, MessagingOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MessagingOptions>, PublisherCacheValidator>());
        services.AddOptions<MessagingOptions>().ValidateOnStart();
        AddRabbitInfrastructure(services);
        AddAzureServiceBusInfrastructure(services);

        var publisherKey = MessageBrokerKeys.Publisher<TFamily>(name);
        // MessageBrokerSerializerOptions is user-facing and stays scoped by topic name — one JSON
        // pipeline per topic, shared across every family that publishes on it.
        services.TryAddKeyedSingleton<IMessageSerializer>(publisherKey, (sp, _) =>
            new SystemTextJsonMessageSerializer(
                sp.GetRequiredService<IOptionsMonitor<MessageBrokerSerializerOptions>>(), name));
        services.TryAddKeyedSingleton<ChannelTopic<TFamily, TCeiling>>(publisherKey, (sp, _) =>
            new ChannelTopic<TFamily, TCeiling>(
                sp.GetRequiredService<IOptionsMonitor<MessageTopicOptions<TFamily>>>().Get(name).SubscriptionNames,
                sp.GetServices<ChannelEscrowRegistration<TFamily, TCeiling>>(),
                sp.GetRequiredService<MessageBrokerMetrics>(),
                name));
        services.TryAddKeyedSingleton<Publisher<TFamily, TCeiling>>(publisherKey, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptions<MessagingOptions>>().Value;
            var serializer = sp.GetRequiredKeyedService<IMessageSerializer>(publisherKey);
            var metrics = sp.GetRequiredService<MessageBrokerMetrics>();
            if (!string.IsNullOrEmpty(options.AzureServiceBusConnectionString))
            {
                return new AzureServiceBusPublisher<TFamily, TCeiling>(sp.GetRequiredService<AzureServiceBusConnection>().Client, name, serializer, metrics);
            }
            if (!string.IsNullOrEmpty(options.RabbitUri))
            {
                return new RabbitPublisher<TFamily, TCeiling>(sp.GetRequiredService<RabbitConnection>(), name, serializer, metrics);
            }
            return new ChannelPublisher<TFamily, TCeiling>(sp.GetRequiredKeyedService<ChannelTopic<TFamily, TCeiling>>(publisherKey), name, metrics,
                options.MaxDeliveryCount, sp.GetRequiredService<ILogger<ChannelPublisher<TFamily, TCeiling>>>());
        });

        services.AddSingleton(new BrokerTopologyDeclaration(name, TFamily.WireName));
        services.AddSingleton(new PublisherRoleMarker(
            name,
            TFamily.WireName,
            TFamily.Variants.Select(v => v.WireName).ToHashSet()));
        // Coordinator and requester both run their startup work in the IHostedLifecycleService
        // StartingAsync phase, which the host completes for every service before invoking any
        // StartAsync. However, within the StartingAsync phase itself the host still runs services
        // in registration order, so the coordinator (which brings up the local listener) is registered
        // here first, ahead of the join requester that will send the PublisherJoin.
        AddNegotiationListenerCoordinator(services);
        AddPublisherJoinRequester(services);

        return services;
    }

    /// <summary>
    /// Registers a keyed <see cref="ISubscriber{TFamily, TCeiling}"/> for the given topic name.
    /// </summary>
    /// <remarks>
    /// Each unique <paramref name="subscriptionName"/> receives every message independently,
    /// while multiple instances sharing the same subscription name compete for each message
    /// (pub/sub fan-out with per-group competing consumers). The resolved service key is
    /// <c>name/subscriptionName</c>.
    /// <para>
    /// Set <paramref name="proceedOnAdmissionTimeout"/> to opt this subscriber into graceful
    /// degradation: if the publisher fleet does not respond to the startup capability request
    /// within <see cref="NegotiationOptions.AdmissionTimeout"/>, the requester logs an error
    /// and lets the host start anyway instead of failing. Broker-unreachable and explicit no-go
    /// responses still hard-fail. Multiple registrations for the same topic combine with
    /// strictest wins: a topic only softens if every registration opts in, since silently
    /// starting a hard-requiring subscriber without confirmed compatibility is undefined
    /// behavior.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSubscriber<TFamily, TCeiling>(
        this IServiceCollection services,
        string name,
        string subscriptionName,
        bool proceedOnAdmissionTimeout = false)
        where TFamily : FamilyCeiling<TFamily, TCeiling>, IFamilyVariants<TFamily>, IFamilyWireName<TFamily>
        where TCeiling : Family<TFamily>.ICeiling
    {
        // DI time enforcement of valid family variant chain.
        ChainValidator<TFamily, TCeiling>.ThrowIfInvalid();
        ClaimTopicFamily<TFamily>(services, name);
        ClaimTopicSubscription<TFamily>(services, name, subscriptionName);
        var subscriptionKey = $"{name}/{subscriptionName}";
        var publisherKey = MessageBrokerKeys.Publisher<TFamily>(name);

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
        services.Configure<MessageTopicOptions<TFamily>>(name, opts => opts.SubscriptionNames.Add(subscriptionName));
        services.TryAddKeyedSingleton<ChannelTopic<TFamily, TCeiling>>(publisherKey, (sp, _) =>
            new ChannelTopic<TFamily, TCeiling>(
                sp.GetRequiredService<IOptionsMonitor<MessageTopicOptions<TFamily>>>().Get(name).SubscriptionNames,
                sp.GetServices<ChannelEscrowRegistration<TFamily, TCeiling>>(),
                sp.GetRequiredService<MessageBrokerMetrics>(),
                name));
        // Register ChannelTopic as IHostedService exactly once per (TFamily, name) so it stops
        // last in the LIFO shutdown sequence — after all consumers. ChannelTopic.StopAsync drains
        // remaining channel messages to escrow before sealing writers. We use a private marker
        // type to detect duplicates independently of the keyed-singleton registration (which
        // AddPublisher may have already created for the same topic).
        var marker = new ChannelTopicLifetimeMarker(typeof(TFamily), name);
        if (!services.Any(d => d.ServiceType == typeof(ChannelTopicLifetimeMarker)
                               && marker.Equals(d.ImplementationInstance)))
        {
            services.AddSingleton(marker);
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredKeyedService<ChannelTopic<TFamily, TCeiling>>(publisherKey));
        }

        services.TryAddKeyedSingleton<ISubscriber<TFamily, TCeiling>>(subscriptionKey, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptions<MessagingOptions>>().Value;
            var serializer = sp.GetRequiredKeyedService<IMessageSerializer>(publisherKey);
            var metrics = sp.GetRequiredService<MessageBrokerMetrics>();
            if (!string.IsNullOrEmpty(options.AzureServiceBusConnectionString))
            {
                return new AzureServiceBusSubscriber<TFamily, TCeiling>(sp.GetRequiredService<AzureServiceBusConnection>().Client, name, subscriptionName, serializer, metrics);
            }
            if (!string.IsNullOrEmpty(options.RabbitUri))
            {
                return new RabbitSubscriber<TFamily, TCeiling>(sp.GetRequiredService<RabbitConnection>(), name, subscriptionName, serializer, metrics);
            }
            return new ChannelSubscriber<TFamily, TCeiling>(sp.GetRequiredKeyedService<ChannelTopic<TFamily, TCeiling>>(publisherKey).GetOrAddSubscription(subscriptionName).Reader, name, metrics);
        });

        services.AddSingleton(new ChannelSubscriberDescriptor(typeof(TFamily), subscriptionKey));

        services.AddSingleton(new BrokerTopologyDeclaration(name, TFamily.WireName, subscriptionName));

        services.AddSingleton(new SubscriberRoleMarker(
            name,
            TFamily.WireName,
            TFamily.Variants.Select(v => v.WireName).ToHashSet(),
            proceedOnAdmissionTimeout));
        AddSubscriberCapabilityRequester(services);

        return services;
    }

    /// <summary>
    /// Registers a keyed <see cref="ISubscriber{TFamily, TCeiling}"/> and a <typeparamref name="TConsumer"/>
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
    /// do not take <see cref="ISubscriber{TFamily, TCeiling}"/> as a constructor parameter —
    /// <see cref="ConsumerBackgroundService{TFamily, TCeiling, TConsumer}"/> owns the subscription
    /// and invokes <see cref="IMessageConsumer{TFamily, TCeiling}.HandleAsync"/> for each delivered
    /// message.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMessageConsumer<TFamily, TCeiling, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TConsumer>(
        this IServiceCollection services,
        string name,
        string subscriptionName,
        bool proceedOnAdmissionTimeout = false)
        where TFamily : FamilyCeiling<TFamily, TCeiling>, IFamilyVariants<TFamily>, IFamilyWireName<TFamily>
        where TCeiling : Family<TFamily>.ICeiling
        where TConsumer : class, IMessageConsumer<TFamily, TCeiling>
    {
        var subscriptionKey = $"{name}/{subscriptionName}";
        var publisherKey = MessageBrokerKeys.Publisher<TFamily>(name);

        services.AddSubscriber<TFamily, TCeiling>(name, subscriptionName, proceedOnAdmissionTimeout);

        // Register ChannelEscrowRegistration so ChannelTopic resolves it via
        // IEnumerable<ChannelEscrowRegistration<T>> and wires up startup recovery and shutdown
        // drain callbacks. Keyed by subscriptionKey for deduplication; also registered unkeyed
        // so DI collects all instances when resolving the enumerable.
        var escrowIsNew = !services.Any(d => d.ServiceType == typeof(ChannelEscrowRegistration<TFamily, TCeiling>) && (string?)d.ServiceKey == subscriptionKey);
        services.TryAddKeyedSingleton<ChannelEscrowRegistration<TFamily, TCeiling>>(subscriptionKey, (sp, _) =>
            new ChannelEscrowRegistration<TFamily, TCeiling>(
                name, subscriptionName, subscriptionKey,
                sp.GetRequiredKeyedService<IMessageSerializer>(publisherKey),
                sp.GetRequiredService<IOptions<MessagingOptions>>(),
                sp.GetService<IMessageEscrowStore>(),
                sp.GetRequiredService<ILogger<ChannelEscrowRegistration<TFamily, TCeiling>>>()));
        if (escrowIsNew)
            services.AddSingleton<ChannelEscrowRegistration<TFamily, TCeiling>>(sp =>
                sp.GetRequiredKeyedService<ChannelEscrowRegistration<TFamily, TCeiling>>(subscriptionKey));

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
            d.ServiceType == typeof(ConsumerBackgroundService<TFamily, TCeiling, TConsumer>) &&
            (string?)d.ServiceKey == subscriptionKey);
        services.TryAddKeyedSingleton<ConsumerBackgroundService<TFamily, TCeiling, TConsumer>>(subscriptionKey, (sp, _) =>
            new ConsumerBackgroundService<TFamily, TCeiling, TConsumer>(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredKeyedService<ISubscriber<TFamily, TCeiling>>(subscriptionKey)));
        if (consumerIsNew)
            services.AddSingleton<IHostedService>(sp =>
                sp.GetRequiredKeyedService<ConsumerBackgroundService<TFamily, TCeiling, TConsumer>>(subscriptionKey));

        services.AddSingleton(new ChannelConsumerDescriptor(typeof(TFamily), subscriptionKey));
        // The validator is registered here (not in AddSubscriber) so it only activates when the app
        // has opted into the MessageConsumer framework. Raw AddSubscriber usage (tests, out-of-process
        // consumers on ASB/Rabbit) is not subject to this enforcement.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ChannelConsumerValidationService>());
        return services;
    }

    // Sentinel registered in DI to prevent duplicate IHostedService entries for the same ChannelTopic.
    private sealed record ChannelTopicLifetimeMarker(Type MessageType, string TopicName);

    // Sentinel binding a (topic, family wire name) pair to the CLR family type that owns it.
    // A topic can carry multiple families, but each family's wire name must identify exactly one
    // family shape — two publishers stamping the same routing key on different family types would
    // silently misroute at the subscriber.
    private sealed record TopicFamilyClaim(string TopicName, string FamilyWireName, Type FamilyType);

    // Sentinel binding a (topic, subscription) pair to the CLR family type that reads it. Each
    // subscription is operationally single-family (the broker binding or filter narrows it to
    // one wire name), so registering a second TFamily against the same subscription would
    // deliver one broker queue into two incompatible decoders.
    private sealed record TopicSubscriptionClaim(string TopicName, string SubscriptionName, Type FamilyType);

    private static void ClaimTopicFamily<TFamily>(IServiceCollection services, string name)
        where TFamily : IFamilyWireName<TFamily>
    {
        var wireName = TFamily.WireName;
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType != typeof(TopicFamilyClaim)) continue;
            if (descriptor.ImplementationInstance is not TopicFamilyClaim claim) continue;
            if (claim.TopicName != name || claim.FamilyWireName != wireName) continue;
            if (claim.FamilyType == typeof(TFamily)) return;
            throw new InvalidOperationException(
                $"Topic '{name}' already has family wire name '{wireName}' claimed by family " +
                $"'{claim.FamilyType.Name}'. Cannot also claim it for '{typeof(TFamily).Name}'. " +
                "Each family wire name must identify exactly one family shape within a topic.");
        }
        services.AddSingleton(new TopicFamilyClaim(name, wireName, typeof(TFamily)));
    }

    private static void ClaimTopicSubscription<TFamily>(IServiceCollection services, string name, string subscriptionName)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType != typeof(TopicSubscriptionClaim)) continue;
            if (descriptor.ImplementationInstance is not TopicSubscriptionClaim claim) continue;
            if (claim.TopicName != name || claim.SubscriptionName != subscriptionName) continue;
            if (claim.FamilyType == typeof(TFamily)) return;
            throw new InvalidOperationException(
                $"Subscription '{subscriptionName}' on topic '{name}' is already registered for " +
                $"family '{claim.FamilyType.Name}'. Cannot also register it for " +
                $"'{typeof(TFamily).Name}'. Each subscription receives exactly one family " +
                "— the broker binding picks which family reaches it.");
        }
        services.AddSingleton(new TopicSubscriptionClaim(name, subscriptionName, typeof(TFamily)));
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

    private static void AddNegotiationListenerCoordinator(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(NegotiationListenerCoordinator)))
            return;
        // Validator is registered but NOT wired to ValidateOnStart — NegotiationOptions is only
        // materialized when a distributed backend spawns a transport, so validation naturally
        // scopes to that path and doesn't fire for channel-backend deployments.
        services.AddOptions<NegotiationOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<NegotiationOptions>, NegotiationOptionsValidator>());
        services.TryAddSingleton<NegotiationMetrics>();
        services.AddSingleton<NegotiationListenerCoordinator>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<NegotiationListenerCoordinator>());
    }

    private static void AddPublisherJoinRequester(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(PublisherJoinRequester)))
            return;
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<NegotiationOptions>, NegotiationRoleOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        // Factory that closes over messaging + negotiation options and picks a transport per
        // backend. Registered as a DI service so tests can swap it for an in-memory transport
        // and drive PublisherJoinRequester end-to-end without a real broker.
        services.TryAddSingleton<PublisherNegotiationSenderFactory>(sp => topics =>
        {
            var msgOpts = sp.GetRequiredService<IOptions<MessagingOptions>>();
            var negOpts = sp.GetRequiredService<IOptions<NegotiationOptions>>();
            var messaging = msgOpts.Value;
            if (!string.IsNullOrEmpty(messaging.AzureServiceBusConnectionString))
                return AzureServiceBusNegotiationTransport.ForSender(msgOpts, negOpts);
            if (!string.IsNullOrEmpty(messaging.RabbitUri))
                return RabbitNegotiationTransport.ForPublisherSender(sp.GetRequiredService<RabbitConnection>(), negOpts, topics);
            return new NoopNegotiationTransport();
        });
        // Callers of this helper must register AddNegotiationListenerCoordinator first — see the
        // ordered call in AddPublisher. Within the StartingAsync phase the host still walks
        // services in registration order, so the listener needs to be brought up before this
        // requester sends its first PublisherJoin.
        services.AddSingleton<PublisherJoinRequester>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<PublisherJoinRequester>());
    }

    private static void AddSubscriberCapabilityRequester(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(SubscriberJoinRequester)))
            return;
        services.AddOptions<NegotiationOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<NegotiationOptions>, NegotiationOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<NegotiationOptions>, NegotiationRoleOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        // Subscriber-only services never call AddPublisher (which drags in the coordinator), so
        // register the metrics singleton here too. TryAdd makes it a no-op when the coordinator's
        // registration already ran.
        services.TryAddSingleton<NegotiationMetrics>();
        // Factory that closes over messaging + negotiation options and picks a transport per
        // backend. Registered as a DI service so tests can swap it for an in-memory transport
        // and drive SubscriberJoinRequester end-to-end without a real broker.
        services.TryAddSingleton<SubscriberNegotiationSenderFactory>(sp => topics =>
        {
            var msgOpts = sp.GetRequiredService<IOptions<MessagingOptions>>();
            var negOpts = sp.GetRequiredService<IOptions<NegotiationOptions>>();
            var messaging = msgOpts.Value;
            if (!string.IsNullOrEmpty(messaging.AzureServiceBusConnectionString))
                return AzureServiceBusNegotiationTransport.ForSender(msgOpts, negOpts);
            return RabbitNegotiationTransport.ForSubscriberSender(sp.GetRequiredService<RabbitConnection>(), negOpts);
        });
        services.AddSingleton<SubscriberJoinRequester>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<SubscriberJoinRequester>());
    }

}
