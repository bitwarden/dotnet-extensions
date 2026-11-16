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
/// doesn't wait on them. What it must not do is look healthy meanwhile, so the readiness probe
/// fails, naming each one, until every consumer is receiving.
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
        return reasons.Count == 0 ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy(string.Join(" ", reasons));
    }
}

/// <summary>Puts the consumers' state in front of the readiness probe.</summary>
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

    public void Configure(HealthCheckServiceOptions options) =>
        options.Registrations.Add(new HealthCheckRegistration(
            Name,
            provider => new SubscriptionHealthCheck(provider.GetRequiredService<SubscriptionReadiness>()),
            failureStatus: null,
            tags: null));
}
