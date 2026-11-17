using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// Registers <see cref="OutboxHealthCheck{TContext}"/> once per database.
/// </summary>
internal sealed class OutboxHealthCheckRegistration<TContext> : IConfigureOptions<HealthCheckServiceOptions>
    where TContext : DbContext, IMessageOutboxContext
{
    /// <summary>
    /// <c>HealthCheckTags.Monitoring</c> in Bitwarden.Server.Sdk.HealthChecks, which this package doesn't reference.
    /// </summary>
    internal const string MonitoringTag = "monitoring";

    public static string Name => $"Message outbox ({typeof(TContext).Name})";

    public void Configure(HealthCheckServiceOptions options) =>
        options.Registrations.Add(new HealthCheckRegistration(
            Name,
            provider => ActivatorUtilities.CreateInstance<OutboxHealthCheck<TContext>>(provider),
            // An unreadable database only means the backlog is unknown here.
            failureStatus: HealthStatus.Degraded,
            tags: [MonitoringTag]));
}
