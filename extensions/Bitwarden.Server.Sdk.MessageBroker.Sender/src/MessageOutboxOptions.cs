namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// How the outbox's dispatcher sends what is waiting.
/// </summary>
public sealed class MessageOutboxOptions
{
    /// <summary>
    /// Whether this instance runs the dispatcher. Default true. When false, what is written to the outbox waits there
    /// until an instance that dispatches sends it, which suits a host that only writes, or a test that hands the
    /// service's own messages to their handlers itself rather than racing the dispatcher's polling.
    /// </summary>
    public bool Dispatch { get; set; } = true;

    /// <summary>
    /// How long the dispatcher waits between polls when the last batch wasn't full. Default one second.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How many messages one poll claims. Default 100.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// How long an instance's claim on a message lasts before another may take it. Default thirty seconds.
    /// </summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The longest wait between attempts; the wait doubles from two seconds. Default five minutes.
    /// </summary>
    public TimeSpan MaximumBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The largest serialized message <c>SendAsync</c> accepts. A larger one is refused when it is
    /// written, so the caller's change fails with it, rather than stranding a committed message no
    /// broker would take. Default 256 KiB, Azure Service Bus Standard's limit; raise it on a broker
    /// that allows more. A message the service handles itself (<see cref="MessageOutbox.Local"/>) never reaches a
    /// broker, and has no limit.
    /// </summary>
    public int MaxMessageSize { get; set; } = 256 * 1024;

    /// <summary>
    /// How often the outbox is counted for its metrics and health check, however often they ask.
    /// Default thirty seconds.
    /// </summary>
    public TimeSpan BacklogSampleInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How late the oldest due message may be before the health check reports degraded. Default ten minutes.
    /// </summary>
    public TimeSpan BacklogDegradedAge { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How late the oldest due message may be before the health check reports unhealthy. Default one hour.
    /// </summary>
    public TimeSpan BacklogUnhealthyAge { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Whether to wake the dispatcher as soon as a save that wrote outbox messages commits, rather
    /// than leaving them for the next poll. Off by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a service whose readers expect a message's effect within moments of the change that sent
    /// it, sooner than <see cref="PollInterval"/> allows, without polling more often. Only this
    /// instance's dispatcher is woken, and only by saves made in this instance.
    /// </para>
    /// <para>
    /// Changes nothing about delivery: messages are still written in the same save and sent by the
    /// same dispatcher, and the poll still runs as the backstop, so a missed wake-up only means the
    /// next poll sends them.
    /// </para>
    /// </remarks>
    public bool SendOnCommit { get; set; }
}
