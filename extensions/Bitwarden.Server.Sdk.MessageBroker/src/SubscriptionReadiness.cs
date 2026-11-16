using System.Collections.Concurrent;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Whether each hosted consumer's subscription or command queue is receiving.
/// </summary>
/// <remarks>
/// <para>
/// A service starts whether or not its consumers can subscribe, because most of what it does
/// doesn't wait on them, and its consumers catch up once the broker is back. So the check reports
/// <see cref="HealthStatus.Degraded"/>, naming each consumer not receiving, never Unhealthy: a broker
/// every instance reads is shared, and taking instances out of rotation can't fix it, but would turn
/// a delay in the background into an outage in the foreground. Degraded keeps a default health
/// endpoint answering 200, and the check is tagged <c>monitoring</c>, as Bitwarden.Server.Sdk.HealthChecks
/// tags checks of shared state, so a host can show it without letting it gate readiness.
/// </para>
/// <para>
/// A consumer counts as receiving on a positive signal from its backend that the subscription is in
/// place (see <see cref="ISignalsSubscribed{TPayload, TCeiling}"/>), never on time passing: a
/// broker whose address doesn't answer can take longer to fail than any wait would allow. The
/// reasons name what is missing and nothing about the broker: the probe is reachable without a
/// credential, and a host name or an exception's text is for the log.
/// </para>
/// </remarks>
internal sealed class SubscriptionReadiness
{
    private readonly ConcurrentDictionary<string, string?> _sources = new(StringComparer.Ordinal);

    /// <summary>A consumer that has not started receiving yet.</summary>
    public void Expect(string source) =>
        _sources.TryAdd(source, $"'{source}' has not started receiving yet.");

    /// <summary>It is receiving.</summary>
    public void Receiving(string source) => _sources[source] = null;

    /// <summary>Its stream failed; it is subscribing again.</summary>
    public void Failing(string source) =>
        _sources[source] = $"'{source}' cannot receive from the message broker, and is trying again.";

    public HealthCheckResult Check()
    {
        var reasons = _sources.Values.Where(reason => reason is not null).Order(StringComparer.Ordinal).ToList();
        return reasons.Count == 0 ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded(string.Join(" ", reasons));
    }
}

/// <summary>Reports the consumers' state to the host's health checks.</summary>
internal sealed class SubscriptionHealthCheck(SubscriptionReadiness readiness) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(readiness.Check());
}

/// <summary>
/// Registers <see cref="SubscriptionHealthCheck"/> once, however many consumers there are, for any
/// host that calls <c>AddHealthChecks()</c>.
/// </summary>
internal sealed class SubscriptionHealthCheckRegistration : IConfigureOptions<HealthCheckServiceOptions>
{
    public const string Name = "Message broker subscriptions";

    /// <summary>The tag for shared state, as <c>HealthCheckTags.Monitoring</c> in Bitwarden.Server.Sdk.HealthChecks.</summary>
    public const string MonitoringTag = "monitoring";

    public void Configure(HealthCheckServiceOptions options) =>
        options.Registrations.Add(new HealthCheckRegistration(
            Name,
            provider => new SubscriptionHealthCheck(provider.GetRequiredService<SubscriptionReadiness>()),
            failureStatus: HealthStatus.Degraded,
            tags: [MonitoringTag]));
}
