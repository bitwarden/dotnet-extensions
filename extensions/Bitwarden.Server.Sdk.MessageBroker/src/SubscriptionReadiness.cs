using System.Collections.Concurrent;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Whether each hosted consumer's subscription or command queue is receiving.
/// </summary>
/// <remarks>
/// <para>
/// Reports <see cref="HealthStatus.Degraded"/>, never Unhealthy, and is tagged <c>monitoring</c>: a
/// broker every instance reads is shared, so it must not take instances out of rotation.
/// </para>
/// <para>
/// A consumer counts as receiving only on a positive signal from its backend
/// (<see cref="ISignalsSubscribed{TPayload, TCeiling}"/>), never on time passing. The reasons name
/// nothing about the broker: the probe is reachable without a credential.
/// </para>
/// </remarks>
internal sealed class SubscriptionReadiness
{
    private readonly ConcurrentDictionary<string, string?> _sources = new(StringComparer.Ordinal);

    /// <summary>
    /// A consumer that has not started receiving yet.
    /// </summary>
    public void Expect(string source) =>
        _sources.TryAdd(source, $"'{source}' has not started receiving yet.");

    public void Receiving(string source) => _sources[source] = null;

    /// <summary>
    /// Its stream failed; it is subscribing again.
    /// </summary>
    public void Failing(string source) =>
        _sources[source] = $"'{source}' cannot receive from the message broker, and is trying again.";

    public HealthCheckResult Check()
    {
        var reasons = _sources.Values.Where(reason => reason is not null).Order(StringComparer.Ordinal).ToList();
        return reasons.Count == 0 ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded(string.Join(" ", reasons));
    }
}

/// <summary>
/// Reports the consumers' state to the host's health checks.
/// </summary>
internal sealed class SubscriptionHealthCheck(SubscriptionReadiness readiness) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(readiness.Check());
}

/// <summary>
/// Registers <see cref="SubscriptionHealthCheck"/> once for any host that calls <c>AddHealthChecks()</c>.
/// </summary>
internal sealed class SubscriptionHealthCheckRegistration : IConfigureOptions<HealthCheckServiceOptions>
{
    public const string Name = "Message broker subscriptions";

    /// <summary>
    /// The tag for shared state, as <c>HealthCheckTags.Monitoring</c> in Bitwarden.Server.Sdk.HealthChecks.
    /// </summary>
    public const string MonitoringTag = "monitoring";

    public void Configure(HealthCheckServiceOptions options) =>
        options.Registrations.Add(new HealthCheckRegistration(
            Name,
            provider => new SubscriptionHealthCheck(provider.GetRequiredService<SubscriptionReadiness>()),
            failureStatus: HealthStatus.Degraded,
            tags: [MonitoringTag]));
}
