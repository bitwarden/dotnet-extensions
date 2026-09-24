using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Reconciles the SQL filter rules on the negotiation request subscription against the
/// per-topic rule set derived from <see cref="PublisherRoleMarker"/>s. Runs once at coordinator
/// startup for the Azure Service Bus backend and brings the subscription's rule set to exactly
/// one <c>[data-topic] = '{topic}'</c> rule per served topic.
/// <para>
/// This is the ASB analog to <see cref="RabbitNegotiationTransport"/>'s per-topic queue bindings
/// (declared inline in <c>EnsureAsync</c>). On Rabbit the primitive is a binding declaration; on
/// ASB it is a subscription rule managed via <see cref="ServiceBusAdministrationClient"/>.
/// Requires Data Owner rights scoped to the subscription (see §2.2 of the MSA doc).
/// </para>
/// <para>
/// The Azure Service Bus emulator does not support the management REST API, so
/// <see cref="ReconcileAsync"/> cannot be exercised against it in CI. The diff computation
/// is factored out into <see cref="ComputeDiff"/> for unit-level coverage; the SDK wrapper
/// remains untested at CI level.
/// </para>
/// </summary>
internal static class AzureServiceBusRuleReconciler
{
    private const string DataTopicPropertyName = "data-topic";
    private const string RuleNamePrefix = "data-topic-";

    /// <summary>
    /// Reads current rules on <paramref name="subscription"/>, computes the diff against the
    /// desired per-topic rule set, and applies additions + removals so the final state is
    /// exactly one rule per topic.
    /// </summary>
    public static async Task ReconcileAsync(
        ServiceBusAdministrationClient admin,
        string topic,
        string subscription,
        IReadOnlyCollection<string> dataTopics,
        CancellationToken cancellationToken = default)
    {
        var current = new List<string>();
        await foreach (var rule in admin.GetRulesAsync(topic, subscription, cancellationToken))
            current.Add(rule.Name);

        var (toAdd, toRemove) = ComputeDiff(current, dataTopics);

        // Reconciliation runs in every pod's StartingAsync, so concurrent boots (rolling deploys,
        // multiple replicas coming up together) will race on the same add/remove operations.
        // Swallow the expected idempotency failures so the losing pod still starts.
        foreach (var name in toRemove)
        {
            try { await admin.DeleteRuleAsync(topic, subscription, name, cancellationToken); }
            catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound) { }
        }
        foreach (var name in toAdd)
        {
            try
            {
                await admin.CreateRuleAsync(topic, subscription,
                    new CreateRuleOptions(name, DesiredFilter(NameToTopic(name))), cancellationToken);
            }
            catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists) { }
        }
    }

    /// <summary>
    /// Pure diff between the subscription's current rule names and the desired per-topic rule
    /// set. Extracted so it can be unit-tested without a broker (the ASB emulator does not
    /// support the management REST API).
    /// </summary>
    internal static (List<string> ToAdd, List<string> ToRemove) ComputeDiff(
        IEnumerable<string> currentRuleNames,
        IReadOnlyCollection<string> dataTopics)
    {
        var desired = new HashSet<string>(dataTopics.Select(RuleName));
        var current = new HashSet<string>(currentRuleNames);
        return (
            ToAdd: [.. desired.Except(current)],
            ToRemove: [.. current.Except(desired)]);
    }

    private static string RuleName(string dataTopic) => $"{RuleNamePrefix}{dataTopic}";

    private static string NameToTopic(string ruleName) => ruleName[RuleNamePrefix.Length..];

    private static SqlRuleFilter DesiredFilter(string dataTopic)
    {
        var filter = new SqlRuleFilter($"[{DataTopicPropertyName}] = @topic");
        filter.Parameters.Add("@topic", dataTopic);
        return filter;
    }
}
