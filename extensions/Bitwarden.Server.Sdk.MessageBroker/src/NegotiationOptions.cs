namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Options for message-plane version negotiation. See the <c>Version negotiation</c> section of
/// the package documentation for provisioning and permissions.
/// </summary>
public sealed class NegotiationOptions
{
    /// <summary>
    /// <para>Required.</para>
    /// Identifier for this service. Derives <see cref="RequestSubscriptionName"/> and
    /// <see cref="ReplySubscriptionName"/> and tags negotiation metrics. Must be unique per
    /// messaging namespace.
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

    /// <summary>Namespace-wide control topic name. Defaults to <c>"ctrl"</c>.</summary>
    public string ControlTopicName { get; set; } = "ctrl";

    /// <summary>Capability republish interval. Defaults to ninety seconds.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Multiplier applied to <see cref="HeartbeatInterval"/> for the cache entry TTL. Must be
    /// greater than 1.0. Defaults to 5.5, so an instance survives up to four consecutive missed
    /// heartbeats before the fleet considers it gone.
    /// </summary>
    public double TtlPaddingFactor { get; set; } = 5.5;

    /// <summary>
    /// Startup admission timeout: the requester exits non-zero after this window without a
    /// reply. Defaults to thirty seconds.
    /// </summary>
    public TimeSpan AdmissionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Derived <c>"request-{ServiceName}"</c>. Subscription name this service consumes admission
    /// requests from.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="ServiceName"/> is not set.</exception>
    public string RequestSubscriptionName
        => $"request-{ServiceName ?? throw ServiceNameNotSet()}";

    /// <summary>
    /// Derived <c>"reply-{ServiceName}"</c>. Subscription name this service consumes admission
    /// replies from. Unused on RabbitMQ, which uses the per-connection <c>amq.rabbitmq.reply-to</c>
    /// pseudo-queue.
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
