namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// What waited in an outbox when it was last counted.
/// </summary>
/// <param name="Due">Messages due and not yet sent.</param>
/// <param name="OldestLateness">How long the longest-waiting of them has waited past when it was due; null when none.</param>
/// <param name="SetAside">Messages set aside.</param>
/// <param name="TakenAt">When.</param>
internal sealed record OutboxBacklogSample(long Due, TimeSpan? OldestLateness, long SetAside, DateTimeOffset TakenAt);
