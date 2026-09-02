using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Database;

internal sealed class DatabaseMigrationHostedService : IHostedService
{
    private const int MaxAttempts = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IOptionsMonitor<AutoMigrateOptions> _migrationOptions;
    private readonly ILogger<DatabaseMigrationHostedService> _logger;

    public DatabaseMigrationHostedService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptionsMonitor<AutoMigrateOptions> migrationOptions,
        ILogger<DatabaseMigrationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _migrationOptions = migrationOptions;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var migrators = scope.ServiceProvider.GetKeyedServices<IDatabaseMigrator>(KeyedService.AnyKey);

        foreach (var migrator in migrators)
        {
            // Each schema decides for itself, so a host can own one schema and only consume another.
            if (!_migrationOptions.Get(migrator.Name).AutoMigrate)
            {
                _logger.LogDebug(
                    "Automatic database migrations are disabled for {Name}.", migrator.Name);
                continue;
            }

            // Only the initial phase belongs at startup: transition scripts are unjournaled and
            // would re-apply on every boot. Nothing routes here on its own — the migrator utility
            // sets the phase on a service collection of its own, which has no hosted service — so
            // this only fires when a host configures SqlServerMigrationOptions.Phase by hand. Warn
            // rather than throw: that is a misconfiguration, not a reason to refuse to start.
            if (migrator is SqlServerMigrator { Phase: not MigrationPhase.Initial } sqlServer)
            {
                _logger.LogWarning(
                    "Skipping {Name}: startup migrations only apply the initial phase, but {Phase} is configured.",
                    migrator.Name,
                    sqlServer.Phase);
                continue;
            }

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    await migrator.MigrateAsync(cancellationToken);
                    break;
                }
                catch (DbException e)
                {
                    if (attempt >= MaxAttempts)
                    {
                        _logger.LogError(e, "Database {Name} failed to migrate.", migrator.Name);
                        throw;
                    }

                    _logger.LogError(e,
                        "Database {Name} unavailable for migration. Trying again (attempt #{Attempt})...",
                        migrator.Name,
                        attempt + 1);
                    await Task.Delay(TimeSpan.FromSeconds(20), _timeProvider, cancellationToken);
                }
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
