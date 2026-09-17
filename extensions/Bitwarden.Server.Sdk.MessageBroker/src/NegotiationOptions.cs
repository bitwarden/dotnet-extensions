namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Options for message-plane version negotiation. Distributed backends exchange
/// <see cref="Capability"/> and <see cref="PublisherJoin"/> messages on a broker-wide
/// control topic to verify compatibility at deploy time.
/// <para>
/// Names carried here identify entities on that control topic. On Azure Service Bus those
/// entities must be pre-provisioned. On RabbitMQ the library declares them at startup,
/// given <c>configure</c> permission on the name pattern. The in-memory channel backend
/// skips negotiation entirely.
/// </para>
/// </summary>
public sealed class NegotiationOptions
{
    /// <summary>
    /// <para>Required</para>
    /// Identifier for this service. Derives
    /// <see cref="RequestSubscriptionName"/> and <see cref="ReplySubscriptionName"/>,
    /// and tags negotiation metrics.
    /// <para>
    /// Must be unique across the deployment and, for Azure Service Bus, the resulting subscription
    /// names must match deployed infrastructure.
    /// </para>
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Operator-facing label for this process. Defaults to <see cref="Environment.MachineName"/>
    /// (pod name under Kubernetes, hostname on VMs). Combined with <see cref="Id"/> to form
    /// <see cref="InstanceId"/>.
    /// </summary>
    public string? ProcessDisplayName { get; set; } = Environment.MachineName;

    /// <summary>
    /// Optional Guid disambiguating processes that share a <see cref="ProcessDisplayName"/>.
    /// <para>
    /// Invariant: two processes with different capabilities must not share an
    /// <see cref="InstanceId"/>. A fresh Guid per process (the default) always satisfies it;
    /// set a stable value derived from the code to let a restarted instance reuse its identity.
    /// </para>
    /// </summary>
    public Guid? Id { get; set; }

    /// <summary>
    /// Namespace-wide control topic name. Defaults to <c>"ctrl"</c>. Pre-provisioned on Azure
    /// Service Bus; declared by the library at startup on RabbitMQ.
    /// </summary>
    public string ControlTopicName { get; set; } = "ctrl";

    /// <summary>Capability republish interval. Defaults to five minutes.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Multiplier applied to <see cref="HeartbeatInterval"/> for the cache entry TTL. Must be
    /// greater than 1.0. Defaults to 1.2 (one heartbeat's grace).
    /// </summary>
    public double TtlPaddingFactor { get; set; } = 1.2;

    /// <summary>
    /// Startup admission timeout: the requester exits non-zero after this window without a
    /// reply. Defaults to thirty seconds.
    /// </summary>
    public TimeSpan AdmissionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Derived <c>"request-{ServiceName}"</c>. Where this service receives incoming negotiation
    /// requests (<see cref="Capability"/> from subscribers, <see cref="PublisherJoin"/> from
    /// other publishers) that it must ack.
    /// <para>
    /// On Azure Service Bus, pre-provisioned session-enabled (session-id = data-topic) with a
    /// broker filter routing only the data-topics this service publishes. On RabbitMQ, declared
    /// by the library at startup with <c>x-single-active-consumer</c> and one binding per
    /// data-topic registered via <c>AddPublisher</c>.
    /// </para>
    /// <para>
    /// TODO: MDG ASB filter should be applied in-code. permissions to do so are part of the
    /// pre-provisioning requirements.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="ServiceName"/> is not set.</exception>
    public string RequestSubscriptionName
        => $"request-{ServiceName ?? throw ServiceNameNotSet()}";

    /// <summary>
    /// Derived <c>"reply-{ServiceName}"</c>. Where this service's instances read go/no go
    /// replies.
    /// <para>
    /// On Azure Service Bus, pre-provisioned session-enabled (session-id = instance identifier).
    /// Unused on RabbitMQ, which uses the per-connection <c>amq.rabbitmq.reply-to</c>
    /// pseudo-queue.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="ServiceName"/> is not set.</exception>
    public string ReplySubscriptionName
        => $"reply-{ServiceName ?? throw ServiceNameNotSet()}";

    /// <summary>
    /// Cache entry TTL: <see cref="HeartbeatInterval"/> × <see cref="TtlPaddingFactor"/>.
    /// </summary>
    internal TimeSpan CacheEntryTtl
        => TimeSpan.FromTicks((long)(HeartbeatInterval.Ticks * TtlPaddingFactor));

    private string? _resolvedInstanceId;

    /// <summary>
    /// Composite <c>"{ProcessDisplayName}-{Id}"</c>. Materialized once on first read and cached
    /// for the process lifetime; a fresh Guid is generated when <see cref="Id"/> is unset.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="ProcessDisplayName"/> is not set.</exception>
    public string InstanceId
        => LazyInitializer.EnsureInitialized(
            ref _resolvedInstanceId,
            () => $"{ProcessDisplayName ?? throw DisplayNameNotSet()}-{Id ?? Guid.NewGuid()}");

    private static InvalidOperationException ServiceNameNotSet()
        => new($"{nameof(NegotiationOptions)}.{nameof(ServiceName)} must be set before " +
               "subscription names can be resolved.");

    private static InvalidOperationException DisplayNameNotSet()
        => new($"{nameof(NegotiationOptions)}.{nameof(ProcessDisplayName)} must be set before " +
               $"{nameof(InstanceId)} can be resolved.");
}
