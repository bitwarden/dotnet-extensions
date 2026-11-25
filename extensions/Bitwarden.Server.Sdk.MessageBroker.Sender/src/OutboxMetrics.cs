using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// The outbox's metrics: what was sent, what failed, what was set aside, how late messages left,
/// and what is waiting now.
/// </summary>
/// <remarks>
/// Every instance of a service samples the same table, so each reports the same backlog: aggregate
/// the gauges with <c>max</c>, never <c>sum</c>. Tags hold names and counts only (the database's
/// context, the destination, the message type, a failure's exception type), never a message's
/// contents or an error's text.
/// </remarks>
internal sealed class OutboxMetrics
{
    internal const string MeterName = "Bitwarden.Server.Sdk.MessageBroker.Sender";
    internal const string DatabaseTag = "bitwarden.outbox.database";
    internal const string DestinationTag = "messaging.destination.name";
    internal const string PayloadFamilyTag = "messaging.payload.family";

    private readonly ConcurrentDictionary<string, Func<OutboxBacklogSample?>> _backlogs = new(StringComparer.Ordinal);
    private readonly Counter<long> _sent;
    private readonly Counter<long> _failures;
    private readonly Counter<long> _setAside;
    private readonly Histogram<double> _lateness;

    public OutboxMetrics(IMeterFactory meters)
    {
        var meter = meters.Create(MeterName);
        _sent = meter.CreateCounter<long>(
            "bitwarden.outbox.sent.messages",
            unit: "{message}",
            description: "Messages the outbox sent, or handed to a handler in the service, and removed.");
        _failures = meter.CreateCounter<long>(
            "bitwarden.outbox.send.failures",
            unit: "{message}",
            description: "Attempts to send a message that failed; it is tried again later unless set aside.");
        _setAside = meter.CreateCounter<long>(
            "bitwarden.outbox.set_aside.messages",
            unit: "{message}",
            description: "Messages set aside because sending them again can't succeed.");
        _lateness = meter.CreateHistogram(
            "bitwarden.outbox.send.delay",
            unit: "s",
            description: "How long a message waited past when it was due before it was sent.",
            tags: null,
            advice: new InstrumentAdvice<double>
            {
                HistogramBucketBoundaries = [0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 300, 900, 3600],
            });
        meter.CreateObservableGauge(
            "bitwarden.outbox.pending.messages",
            () => Observe(sample => sample.Due),
            unit: "{message}",
            description: "Messages due and not yet sent, as last sampled.");
        meter.CreateObservableGauge(
            "bitwarden.outbox.oldest_pending.age",
            () => Observe(sample => sample.OldestLateness?.TotalSeconds ?? 0d),
            unit: "s",
            description: "How long the longest-waiting due message has waited past when it was due, as last sampled; 0 when none.");
        meter.CreateObservableGauge(
            "bitwarden.outbox.set_aside.pending",
            () => Observe(sample => sample.SetAside),
            unit: "{message}",
            description: "Messages set aside and still in the outbox, as last sampled.");
    }

    public void Register(string database, Func<OutboxBacklogSample?> sample) => _backlogs[database] = sample;

    public void Sent(string database, OutboxMessage message, TimeSpan late)
    {
        var tags = Tags(database, message);
        _sent.Add(1, tags);
        _lateness.Record(Math.Max(0, late.TotalSeconds), tags);
    }

    public void Failed(string database, OutboxMessage message, Exception error)
    {
        var tags = Tags(database, message);
        tags.Add("error.type", error.GetType().FullName);
        _failures.Add(1, tags);
    }

    public void SetAside(string database, OutboxMessage message) => _setAside.Add(1, Tags(database, message));

    private static TagList Tags(string database, OutboxMessage message) => new()
    {
        { DatabaseTag, database },
        { DestinationTag, message.Destination },
        { PayloadFamilyTag, message.MessageType },
    };

    private IEnumerable<Measurement<T>> Observe<T>(Func<OutboxBacklogSample, T> value)
        where T : struct
    {
        foreach (var (database, sample) in _backlogs)
        {
            if (sample() is { } taken)
            {
                yield return new Measurement<T>(value(taken), new KeyValuePair<string, object?>(DatabaseTag, database));
            }
        }
    }
}
