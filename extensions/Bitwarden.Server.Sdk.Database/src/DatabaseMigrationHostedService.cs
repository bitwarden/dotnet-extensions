using System.Data.Common;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Database;

internal sealed class DatabaseMigrationHostedService : IHostedService
{
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(15);

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

            if (migrator is IDatabaseAvailability availability)
                await WaitForDatabaseAsync(migrator.Name, availability, cancellationToken);

            // Migrated once. A failing migration fails the same way every time, since it runs in a
            // transaction, so retrying would only delay the error.
            try
            {
                await migrator.MigrateAsync(cancellationToken);
            }
            catch (DbException e)
            {
                _logger.LogError(
                    "Database {Name} failed to migrate: {Error}.", migrator.Name, DatabaseErrors.Describe(e));
                throw;
            }
        }
    }

    /// <summary>
    /// Waits, with backoff, for the database server to accept a connection, as it may still be
    /// starting alongside this host. Gives up after <see cref="MaxWait"/> with the last error.
    /// </summary>
    private async Task WaitForDatabaseAsync(
        string name, IDatabaseAvailability availability, CancellationToken cancellationToken)
    {
        var deadline = _timeProvider.GetUtcNow() + MaxWait;
        var delay = InitialDelay;

        while (true)
        {
            var error = await availability.CheckConnectionAsync(cancellationToken);
            if (error is null)
                return;

            if (_timeProvider.GetUtcNow() >= deadline)
            {
                _logger.LogError(
                    "Database {Name} did not accept a connection within {MaxWait}: {Error}.",
                    name,
                    MaxWait,
                    DatabaseErrors.Describe(error));
                ExceptionDispatchInfo.Capture(error).Throw();
            }

            _logger.LogWarning(
                "Database {Name} is not accepting connections yet ({Error}); trying again in {Delay}.",
                name,
                DatabaseErrors.Describe(error),
                delay);
            await Task.Delay(delay, _timeProvider, cancellationToken);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxDelay.Ticks));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
