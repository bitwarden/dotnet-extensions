using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// Whether an outbox's messages are leaving: degraded once the longest-waiting due message is
/// <see cref="MessageOutboxOptions.BacklogDegradedAge"/> late, unhealthy once it is
/// <see cref="MessageOutboxOptions.BacklogUnhealthyAge"/> late or any message has been set aside.
/// </summary>
/// <remarks>
/// Tagged <c>monitoring</c>, so a host leaves it out of readiness: every instance drains the same
/// table. The description and data hold counts, ages and the database's name only.
/// </remarks>
internal sealed class OutboxHealthCheck<TContext>(OutboxBacklog<TContext> backlog, IOptions<MessageOutboxOptions> options) : IHealthCheck
    where TContext : DbContext, IMessageOutboxContext
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var name = typeof(TContext).Name;
        var sample = await backlog.SampleAsync(cancellationToken);
        var data = new Dictionary<string, object>
        {
            ["pending"] = sample.Due,
            ["oldestAgeSeconds"] = (long)(sample.OldestLateness?.TotalSeconds ?? 0),
            ["setAside"] = sample.SetAside,
        };
        if (sample.SetAside > 0)
        {
            return HealthCheckResult.Unhealthy(
                $"{sample.SetAside} message(s) in {name}'s outbox were set aside and won't be sent.", data: data);
        }

        if (sample.OldestLateness is not { } late)
        {
            return HealthCheckResult.Healthy(data: data);
        }

        var description = $"{sample.Due} message(s) are due in {name}'s outbox; the oldest is {late:d\\.hh\\:mm\\:ss} late.";
        return late >= options.Value.BacklogUnhealthyAge
            ? HealthCheckResult.Unhealthy(description, data: data)
            : late >= options.Value.BacklogDegradedAge
                ? HealthCheckResult.Degraded(description, data: data)
                : HealthCheckResult.Healthy(data: data);
    }
}
