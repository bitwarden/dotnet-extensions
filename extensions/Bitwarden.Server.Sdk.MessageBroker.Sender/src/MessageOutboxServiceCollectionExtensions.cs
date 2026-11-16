using System.Diagnostics.CodeAnalysis;
using Bitwarden.Server.Sdk.MessageBroker.Sender;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers a service's senders on its outbox.
/// </summary>
public static class MessageOutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="Publisher{TPayload, TCeiling}"/> a service uses to publish events of
    /// type <typeparamref name="TPayload"/> on <paramref name="topic"/>, through its outbox in
    /// <typeparamref name="TContext"/>. Inject it under <c>"{topic}#{WireName}"</c>, as with
    /// <c>AddPublisher</c>; <c>SendAsync</c> adds the message to the outbox, and the caller's
    /// <c>SaveChangesAsync</c> commits it with the change.
    /// </summary>
    public static IServiceCollection AddOutboxPublisher<TPayload, TCeiling, TContext>(this IServiceCollection services, string topic)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
        where TContext : DbContext, IMessageOutboxContext
    {
        ThrowIfRegisteredDirectly<TPayload, TCeiling>(services, topic);
        services.AddDirectPublisher<TPayload, TCeiling>(topic);
        return AddOutboxSender<TPayload, TCeiling, TContext>(services, topic);
    }

    /// <summary>
    /// Registers the <see cref="Publisher{TPayload, TCeiling}"/> a service uses to send commands of
    /// type <typeparamref name="TPayload"/> to another service's command queue
    /// <paramref name="queueName"/>, through its outbox in <typeparamref name="TContext"/>. A command
    /// sent before the queue exists waits in the outbox until it does.
    /// </summary>
    public static IServiceCollection AddOutboxCommandSender<TPayload, TCeiling, TContext>(this IServiceCollection services, string queueName)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
        where TContext : DbContext, IMessageOutboxContext
    {
        ThrowIfRegisteredDirectly<TPayload, TCeiling>(services, queueName);
        services.AddDirectCommandSender<TPayload, TCeiling>(queueName);
        return AddOutboxSender<TPayload, TCeiling, TContext>(services, queueName);
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> to handle messages of type
    /// <typeparamref name="TPayload"/> in this service, through its outbox in
    /// <typeparamref name="TContext"/>: work that must follow a change once it commits, but needn't
    /// happen inside the request. Send with the <see cref="Publisher{TPayload, TCeiling}"/> keyed
    /// <c>"local#{WireName}"</c> (see <see cref="MessageOutbox.Local"/>) and save; the dispatcher
    /// then calls the handler in a scope of its own instead of sending the message to the broker.
    /// </summary>
    /// <remarks>
    /// The handler is an <see cref="IMessageConsumer{TPayload, TCeiling}"/>, settled as a
    /// subscription's would be: returning completes the message, throwing or <c>RequeueAsync</c>
    /// tries it again later with the outbox's backoff, and <c>DeadLetterAsync</c> sets it aside. It
    /// can run more than once for one message, so it must be harmless to repeat.
    /// </remarks>
    public static IServiceCollection AddOutboxLocalHandler<TPayload, TCeiling, TContext, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(this IServiceCollection services)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
        where TContext : DbContext, IMessageOutboxContext
        where THandler : class, IMessageConsumer<TPayload, TCeiling>
    {
        ChainValidator<TPayload, TCeiling>.ThrowIfInvalid();
        var key = MessageBrokerKeys.Publisher<TPayload>(MessageOutbox.Local);
        services.TryAddKeyedSingleton<IMessageSerializer>(key, (sp, _) =>
            new SystemTextJsonMessageSerializer(sp.GetRequiredService<IOptionsMonitor<MessageBrokerSerializerOptions>>(), MessageOutbox.Local));
        services.TryAddScoped<THandler>();
        return AddOutboxSender<TPayload, TCeiling, TContext>(services, MessageOutbox.Local,
            sp => new OutboxLocalRoute<TPayload, TCeiling, THandler>(sp));
    }

    private static IServiceCollection AddOutboxSender<TPayload, TCeiling, TContext>(IServiceCollection services, string destination)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
        where TContext : DbContext, IMessageOutboxContext
        => AddOutboxSender<TPayload, TCeiling, TContext>(services, destination, sp => new OutboxBrokerRoute<TPayload, TCeiling>(destination, sp));

    private static IServiceCollection AddOutboxSender<TPayload, TCeiling, TContext>(
        IServiceCollection services, string destination, Func<IServiceProvider, OutboxRoute> route)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
        where TContext : DbContext, IMessageOutboxContext
    {
        var key = MessageBrokerKeys.Publisher<TPayload>(destination);
        var registration = new OutboxRegistrationKey(destination, TPayload.WireName, typeof(TContext));
        if (services.Any(d => d.ServiceType == typeof(OutboxRegistrationKey) && registration.Equals(d.ImplementationInstance)))
            return services;
        services.AddSingleton(registration);

        // Scoped, like the DbContext it writes to.
        services.AddKeyedScoped<Publisher<TPayload, TCeiling>>(key, (sp, _) => new OutboxPublisher<TPayload, TCeiling, TContext>(
            destination,
            sp.GetRequiredService<TContext>(),
            sp.GetRequiredKeyedService<IMessageSerializer>(key),
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            sp.GetRequiredService<IOptions<MessageOutboxOptions>>().Value.MaxMessageSize));
        services.AddSingleton(sp => new OutboxRegistration(typeof(TContext), route(sp)));
        services.AddSingleton(new OutboxDestination(typeof(TPayload), destination));
        services.TryAddScoped<IOutboxSender, OutboxSender>();

        services.AddOptions<MessageOutboxOptions>();
        AddSendOnCommit<TContext>(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxDispatcher<TContext>>());
        services.TryAddSingleton<OutboxMetrics>();
        services.TryAddSingleton<OutboxBacklog<TContext>>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<HealthCheckServiceOptions>, OutboxHealthCheckRegistration<TContext>>());
        return services;
    }

    // For MessageOutboxOptions.SendOnCommit: the wake-up the dispatcher waits on, and the interceptor
    // that raises it after a commit that wrote outbox messages. Registered for every outbox context,
    // since the option is read at run time; the interceptor does nothing while it is off.
    [UnconditionalSuppressMessage("Trimming", "IL2091",
        Justification = "TContext is the service's own DbContext, registered with AddDbContext, which keeps the members EF Core needs.")]
    private static void AddSendOnCommit<TContext>(IServiceCollection services)
        where TContext : DbContext, IMessageOutboxContext
    {
        if (services.Any(d => d.ServiceType == typeof(OutboxWake<TContext>)))
            return;
        services.AddSingleton<OutboxWake<TContext>>();
        services.AddSingleton<OutboxSendOnCommitInterceptor<TContext>>();
        services.ConfigureDbContext<TContext>((provider, options) =>
            options.AddInterceptors(provider.GetRequiredService<OutboxSendOnCommitInterceptor<TContext>>()));
    }

    // A message type has one way to be sent. A service that registered it straight to the broker
    // (AddPublisher, AddCommandSender) would bypass the outbox, and its send could be lost.
    private static void ThrowIfRegisteredDirectly<TPayload, TCeiling>(IServiceCollection services, string destination)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
    {
        var key = MessageBrokerKeys.Publisher<TPayload>(destination);
        if (services.Any(d => d.ServiceType == typeof(Publisher<TPayload, TCeiling>) && Equals(d.ServiceKey, key) && d.Lifetime == ServiceLifetime.Singleton))
            throw new InvalidOperationException(
                $"'{TPayload.WireName}' to '{destination}' is already registered to send straight to the broker. " +
                "Register it through the outbox only, so a send can't be lost.");
    }

    private sealed record OutboxRegistrationKey(string Destination, string MessageType, Type Context);
}
