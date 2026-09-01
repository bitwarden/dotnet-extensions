using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Bitwarden.Server.Sdk.Database.Tests;

/// <summary>
/// The retry around <see cref="IDatabaseMigrator.MigrateAsync"/>, against a real SQL Server.
/// </summary>
/// <remarks>
/// The loop only reacts to a <see cref="System.Data.Common.DbException"/> whose message SQL Server
/// itself produced, so the interesting cases need a server that fails on demand. Triggers give us
/// that: they raise the message the filter matches, from the same statements the migrator runs.
/// </remarks>
public sealed class SqlServerMigratorRetryTests : IClassFixture<SqlServerFixture>
{
    private const string ScriptPrefix = "Bitwarden.Server.Sdk.Database.Tests.Migrations.SqlServer";
    private const string ScriptUpgradeMode = "Server is in script upgrade mode.";

    private readonly SqlServerFixture _sqlServer;

    public SqlServerMigratorRetryTests(SqlServerFixture sqlServer)
    {
        _sqlServer = sqlServer;
    }

    private async Task<(IDatabaseMigrator Migrator, FakeTimeProvider Time, FakeLogCollector Logs, string ConnectionString)>
        BuildMigratorAsync(string databaseName, Action<SqlServerMigrationOptions>? configure = null)
    {
        var connectionString = await _sqlServer.ConnectionStringForAsync(databaseName);
        var time = new FakeTimeProvider();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddFakeLogging());
        services.Configure<DatabaseOptions>("Test", opts =>
        {
            opts.Provider = DatabaseProvider.SqlServer;
            opts.ConnectionString = connectionString;
        });
        services.Configure<SqlServerMigrationOptions>("Test", opts =>
        {
            opts.ScriptPrefix = ScriptPrefix;
            configure?.Invoke(opts);
        });
        services.AddSqlServerDatabase("Test", typeof(SqlServerMigratorRetryTests).Assembly);

        // Registered last so it wins: the 20 second waits between attempts are not worth serving.
        services.AddSingleton<TimeProvider>(time);

        var provider = services.BuildServiceProvider();

        return (
            provider.GetRequiredKeyedService<IDatabaseMigrator>("Test"),
            time,
            provider.GetRequiredService<FakeLogCollector>(),
            connectionString);
    }

    /// <summary>
    /// Advances the clock until the migration settles. The migrator does real database work between
    /// attempts, so there is no advancing straight to the next delay — it may not exist yet.
    /// </summary>
    private static async Task SettleAsync(FakeTimeProvider time, Task migration)
    {
        for (var i = 0; i < 400 && !migration.IsCompleted; i++)
        {
            time.Advance(TimeSpan.FromSeconds(20));
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
    }

    private async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Fails the creation of one database with <paramref name="message"/>. The create still goes
    /// through — a trigger that raises without rolling back does not undo the statement — so the
    /// next attempt finds the database present and sails past.
    /// </summary>
    private async Task<IAsyncDisposable> FailDatabaseCreationAsync(
        string connectionString, string databaseName, string message)
    {
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString;

        await ExecuteAsync(master, $"""
            CREATE TRIGGER trg_fail_create ON ALL SERVER FOR CREATE_DATABASE AS
            BEGIN
                IF EVENTDATA().value('(/EVENT_INSTANCE/DatabaseName)[1]', 'nvarchar(128)') = N'{databaseName}'
                    RAISERROR('{message}', 16, 1);
            END;
            """);

        return new Cleanup(() => ExecuteAsync(master, "DROP TRIGGER trg_fail_create ON ALL SERVER;"));
    }

    private sealed class Cleanup : IAsyncDisposable
    {
        private readonly Func<Task> _dispose;

        public Cleanup(Func<Task> dispose)
        {
            _dispose = dispose;
        }

        public async ValueTask DisposeAsync() => await _dispose();
    }

    [Fact(Explicit = true)]
    public async Task MigrateAsync_ScriptUpgradeMode_RetriesAndThenMigrates()
    {
        const string DatabaseName = "retry_success";
        var (migrator, time, logs, connectionString) = await BuildMigratorAsync(DatabaseName);

        await using var _ = await FailDatabaseCreationAsync(connectionString, DatabaseName, ScriptUpgradeMode);

        var migration = migrator.MigrateAsync(TestContext.Current.CancellationToken);
        await SettleAsync(time, migration);
        await migration;

        Assert.Contains(
            logs.GetSnapshot(),
            record => record.Message.Contains("script upgrade mode, retrying (attempt 2/9)"));

        // It recovered rather than merely survived: the scripts are applied.
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        using var count = new SqlCommand("SELECT COUNT(1) FROM [dbo].[Migration]", connection);
        Assert.Equal(2, (int)(await count.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    [Fact(Explicit = true)]
    public async Task MigrateAsync_AnotherDatabaseError_IsNotRetried()
    {
        const string DatabaseName = "retry_other_error";
        var (migrator, time, logs, connectionString) = await BuildMigratorAsync(DatabaseName);

        await using var _ = await FailDatabaseCreationAsync(
            connectionString, DatabaseName, "Some other database failure.");

        var migration = migrator.MigrateAsync(TestContext.Current.CancellationToken);
        await SettleAsync(time, migration);

        await Assert.ThrowsAsync<SqlException>(() => migration);
        Assert.DoesNotContain(logs.GetSnapshot(), record => record.Message.Contains("retrying"));
    }

    [Fact(Explicit = true)]
    public async Task MigrateAsync_ScriptUpgradeModeThatNeverClears_GivesUpAfterNineAttempts()
    {
        const string DatabaseName = "retry_exhausted";

        // A journal to fail against, from a migration that ran before the server started misbehaving.
        var (seed, seedTime, _, connectionString) = await BuildMigratorAsync(DatabaseName);
        var seeding = seed.MigrateAsync(TestContext.Current.CancellationToken);
        await SettleAsync(seedTime, seeding);
        await seeding;

        // Fails the journal rewrite, which every attempt performs while preparing the database.
        await ExecuteAsync(connectionString, $"""
            CREATE TRIGGER trg_fail_journal ON [dbo].[Migration] AFTER UPDATE AS
            BEGIN
                RAISERROR('{ScriptUpgradeMode}', 16, 1);
            END;
            """);

        try
        {
            // A previous prefix the current one extends, so the rewrite keeps finding rows to
            // rewrite and the trigger keeps firing — a server that never comes back.
            var (migrator, time, logs, _) = await BuildMigratorAsync(
                DatabaseName,
                opts => opts.PreviousScriptPrefix = "Bitwarden.Server.Sdk.Database.Tests");

            var migration = migrator.MigrateAsync(TestContext.Current.CancellationToken);
            await SettleAsync(time, migration);

            await Assert.ThrowsAsync<SqlException>(() => migration);

            var retries = logs.GetSnapshot().Count(record => record.Message.Contains("script upgrade mode, retrying"));
            Assert.Equal(8, retries);
        }
        finally
        {
            await ExecuteAsync(connectionString, "DROP TRIGGER [trg_fail_journal];");
        }
    }
}
