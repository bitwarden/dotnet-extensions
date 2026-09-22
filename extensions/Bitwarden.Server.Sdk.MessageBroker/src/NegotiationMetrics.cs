using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Metrics for message-plane version negotiation. Uses the same <see cref="MessageBrokerMetrics.MeterName"/>
/// as the data-plane metrics so ops dashboards subscribe to one meter for both.
/// <para>
/// Two signals:
/// <list type="bullet">
/// <item><description><c>messaging.negotiation.admissions</c> — counter, tagged
/// <c>(destination.name, role, result)</c>. Emitted by the SAC-holding
/// <see cref="NegotiationListener"/> on every admission decision. The <c>result=nogo</c>
/// slice is the primary alerting signal (a nogo means a deploying instance was rejected
/// for wire-name incompatibility).</description></item>
/// <item><description><c>messaging.negotiation.topics_served</c> — observable gauge, tagged
/// <c>(destination.name, role)</c>. Emits value 1 for each data-topic this instance
/// publishes to or subscribes from. Populated once at startup from the marker sets.
/// Answers the "what does this pod serve?" ops question with bounded cardinality.
/// </description></item>
/// </list>
/// </para>
/// </summary>
internal sealed class NegotiationMetrics
{
    private const string RolePublisher = "publisher";
    private const string RoleSubscriber = "subscriber";

    private readonly Counter<long> _admissions;
    private readonly ConcurrentDictionary<(string DataTopic, string Role), byte> _topicsServed = new();

    public NegotiationMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MessageBrokerMetrics.MeterName);
        _admissions = meter.CreateCounter<long>(
            "messaging.negotiation.admissions",
            unit: "{admission}",
            description: "Admission decisions processed by this instance's SAC-holding negotiation listener.");
        meter.CreateObservableGauge<long>(
            "messaging.negotiation.topics_served",
            observeValues: ObserveTopicsServed,
            unit: "{topic}",
            description: "One measurement per (data-topic, role) this instance publishes to or subscribes from.");
    }

    /// <summary>Records a subscriber-capability admission decision.</summary>
    public void RecordCapabilityAdmission(string dataTopic, bool go)
        => RecordAdmission(dataTopic, RoleSubscriber, go);

    /// <summary>Records a publisher-join admission decision.</summary>
    public void RecordJoinAdmission(string dataTopic, bool go)
        => RecordAdmission(dataTopic, RolePublisher, go);

    /// <summary>
    /// Registers this instance as a publisher of the given data-topics. Contributes one
    /// measurement per topic to the <c>topics_served</c> gauge for the process lifetime.
    /// </summary>
    public void RegisterPublisherTopics(IEnumerable<string> dataTopics)
    {
        foreach (var topic in dataTopics)
            _topicsServed.TryAdd((topic, RolePublisher), 0);
    }

    /// <summary>Registers this instance as a subscriber of the given data-topics.</summary>
    public void RegisterSubscriberTopics(IEnumerable<string> dataTopics)
    {
        foreach (var topic in dataTopics)
            _topicsServed.TryAdd((topic, RoleSubscriber), 0);
    }

    private void RecordAdmission(string dataTopic, string role, bool go)
        => _admissions.Add(1,
            new KeyValuePair<string, object?>("messaging.destination.name", dataTopic),
            new KeyValuePair<string, object?>("messaging.negotiation.role", role),
            new KeyValuePair<string, object?>("messaging.negotiation.result", go ? "go" : "nogo"));

    private IEnumerable<Measurement<long>> ObserveTopicsServed()
    {
        foreach (var ((topic, role), _) in _topicsServed)
            yield return new Measurement<long>(1,
                new KeyValuePair<string, object?>("messaging.destination.name", topic),
                new KeyValuePair<string, object?>("messaging.negotiation.role", role));
    }
}
