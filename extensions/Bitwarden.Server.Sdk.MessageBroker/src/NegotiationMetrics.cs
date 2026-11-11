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
/// <c>(destination.name, family, role, result)</c>. Emitted by the SAC-holding
/// <see cref="NegotiationListener"/> on every admission decision. The <c>result=nogo</c>
/// slice is the primary alerting signal (a nogo means a deploying instance was rejected
/// for wire-name incompatibility).</description></item>
/// <item><description><c>messaging.negotiation.families_served</c> — observable gauge, tagged
/// <c>(destination.name, family, role)</c>. Emits value 1 for each
/// <c>(data-topic, family)</c> this instance publishes to or subscribes from. Populated once
/// at startup from the marker sets. Answers the "what does this pod serve?" ops question
/// with bounded cardinality.
/// </description></item>
/// </list>
/// </para>
/// </summary>
internal sealed class NegotiationMetrics
{
    private const string RolePublisher = "publisher";
    private const string RoleSubscriber = "subscriber";

    private readonly Counter<long> _admissions;
    private readonly ConcurrentDictionary<(string DataTopic, string FamilyWireName, string Role), byte> _familiesServed = new();

    public NegotiationMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MessageBrokerMetrics.MeterName);
        _admissions = meter.CreateCounter<long>(
            "messaging.negotiation.admissions",
            unit: "{admission}",
            description: "Admission decisions processed by this instance's SAC-holding negotiation listener.");
        meter.CreateObservableGauge<long>(
            "messaging.negotiation.families_served",
            observeValues: ObserveFamiliesServed,
            unit: "{family}",
            description: "One measurement per (data-topic, family, role) this instance publishes to or subscribes from.");
    }

    /// <summary>Records a subscriber-capability admission decision.</summary>
    public void RecordCapabilityAdmission(string dataTopic, string familyWireName, bool go)
        => RecordAdmission(dataTopic, familyWireName, RoleSubscriber, go);

    /// <summary>Records a publisher-join admission decision.</summary>
    public void RecordJoinAdmission(string dataTopic, string familyWireName, bool go)
        => RecordAdmission(dataTopic, familyWireName, RolePublisher, go);

    /// <summary>
    /// Registers this instance as a publisher of the given <c>(data-topic, family)</c> pairs.
    /// Contributes one measurement per pair to the <c>families_served</c> gauge for the
    /// process lifetime.
    /// </summary>
    public void RegisterPublisherFamilies(IEnumerable<(string DataTopic, string FamilyWireName)> pairs)
    {
        foreach (var (topic, family) in pairs)
            _familiesServed.TryAdd((topic, family, RolePublisher), 0);
    }

    /// <summary>Registers this instance as a subscriber of the given <c>(data-topic, family)</c> pairs.</summary>
    public void RegisterSubscriberFamilies(IEnumerable<(string DataTopic, string FamilyWireName)> pairs)
    {
        foreach (var (topic, family) in pairs)
            _familiesServed.TryAdd((topic, family, RoleSubscriber), 0);
    }

    private void RecordAdmission(string dataTopic, string familyWireName, string role, bool go)
        => _admissions.Add(1,
            new KeyValuePair<string, object?>("messaging.destination.name", dataTopic),
            new KeyValuePair<string, object?>(MessageBrokerMetrics.FamilyTag, familyWireName),
            new KeyValuePair<string, object?>("messaging.negotiation.role", role),
            new KeyValuePair<string, object?>("messaging.negotiation.result", go ? "go" : "nogo"));

    private IEnumerable<Measurement<long>> ObserveFamiliesServed()
    {
        foreach (var ((topic, family, role), _) in _familiesServed)
            yield return new Measurement<long>(1,
                new KeyValuePair<string, object?>("messaging.destination.name", topic),
                new KeyValuePair<string, object?>(MessageBrokerMetrics.FamilyTag, family),
                new KeyValuePair<string, object?>("messaging.negotiation.role", role));
    }
}
