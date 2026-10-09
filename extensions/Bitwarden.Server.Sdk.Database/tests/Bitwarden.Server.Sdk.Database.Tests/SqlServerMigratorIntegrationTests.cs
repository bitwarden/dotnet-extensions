using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Bitwarden.Server.Sdk.Database.Tests;

public class SqlServerMigratorIntegrationTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    private const string ScriptPrefix = "Bitwarden.Server.Sdk.Database.Tests.Migrations.SqlServer";
    private const string CreateOrders = "2026-08-12_00_CreateOrders.sql";
    private const string SeedOrder = "2026-08-13_00_SeedOrder.sql";

    /// <summary>Each test gets its own database, which also exercises creating one.</summary>
    private async Task<(IDatabaseMigrator Migrator, string ConnectionString, FakeLogCollector Logs)> BuildMigratorAsync(
        string databaseName,
        Action<SqlServerMigrationOptions>? configure = null)
    {
        var connectionString = await sqlServer.ConnectionStringForAsync(databaseName);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddFakeLogging());
        services.Configure<DatabaseOptions>("Test", opts =>
        {
            opts.Provider = DatabaseProvider.SqlServer;
            opts.ConnectionString = connectionString;
        });
        services.Configure<SqlServerMigrationOptions>("Test", opts =>
        {
            // What the generated registration would set from BitSqlServerScriptPrefix.
            opts.ScriptPrefix = ScriptPrefix;
            configure?.Invoke(opts);
        });
        services.AddSqlServerDatabaseMigrator("Test", typeof(SqlServerMigratorIntegrationTests).Assembly);

        var provider = services.BuildServiceProvider();

        return (
            provider.GetRequiredKeyedService<IDatabaseMigrator>("Test"),
            connectionString,
            provider.GetRequiredService<FakeLogCollector>());
    }

    private static async Task<List<string>> QueryAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        using var cmd = new SqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var rows = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            rows.Add(reader.GetString(0));

        return rows;
    }

    private static Task<List<string>> JournalAsync(string connectionString)
        => QueryAsync(connectionString, "SELECT [ScriptName] FROM [dbo].[Migration] ORDER BY [ScriptName]");

    private static Task<List<string>> OrderNamesAsync(string connectionString)
        => QueryAsync(connectionString, "SELECT [Name] FROM [dbo].[Orders] ORDER BY [Name]");

    [Fact(Explicit = true)]
    public async Task MigrateAsync_CreatesTheDatabase_AppliesScripts_AndJournalsThem()
    {
        var (migrator, connectionString, _) = await BuildMigratorAsync("apply_test");

        await migrator.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["initial"], await OrderNamesAsync(connectionString));

        var journal = await JournalAsync(connectionString);
        Assert.Equal(2, journal.Count);
        Assert.All(journal, name => Assert.StartsWith($"{ScriptPrefix}.", name));
        Assert.Contains(journal, name => name.EndsWith(CreateOrders, StringComparison.Ordinal));
        Assert.Contains(journal, name => name.EndsWith(SeedOrder, StringComparison.Ordinal));

        // Archived and transition scripts are not part of the initial phase.
        Assert.DoesNotContain(journal, name => name.Contains(".Archive.", StringComparison.Ordinal));
        Assert.DoesNotContain(journal, name => name.Contains(".Transition.", StringComparison.Ordinal));
    }

    [Fact(Explicit = true)]
    public async Task MigrateAsync_RunTwice_AppliesNothingTheSecondTime()
    {
        var (migrator, connectionString, _) = await BuildMigratorAsync("rerun_test");

        await migrator.MigrateAsync(TestContext.Current.CancellationToken);
        await migrator.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, (await JournalAsync(connectionString)).Count);
        Assert.Equal(["initial"], await OrderNamesAsync(connectionString));
    }

    [Fact(Explicit = true)]
    public async Task DryRun_ReportsTheScriptsWithoutApplyingThem()
    {
        // The database doesn't exist yet, so preparing it is part of what a dry run has to survive.
        var (migrator, connectionString, logs) = await BuildMigratorAsync(
            "dry_run_test",
            opts => opts.DryRun = true);

        await migrator.MigrateAsync(TestContext.Current.CancellationToken);

        var reported = logs.GetSnapshot().Select(record => record.Message).ToList();
        Assert.Contains(reported, message => message.Contains($"Would apply {ScriptPrefix}.{CreateOrders}"));
        Assert.Contains(reported, message => message.Contains($"Would apply {ScriptPrefix}.{SeedOrder}"));
        Assert.Contains(reported, message => message.Contains("2 script(s) would be applied"));

        // Nothing ran: the journal exists only once a script has been applied.
        Assert.Null(await ScalarOrNullAsync(
            connectionString, "SELECT OBJECT_ID('[dbo].[Migration]', 'U')"));
    }

    [Fact(Explicit = true)]
    public async Task DryRun_AfterMigrating_ReportsNothingPending()
    {
        var (migrator, connectionString, _) = await BuildMigratorAsync("dry_run_applied_test");
        await migrator.MigrateAsync(TestContext.Current.CancellationToken);

        var (dryRun, _, logs) = await BuildMigratorAsync(
            "dry_run_applied_test",
            opts => opts.DryRun = true);

        await dryRun.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Contains(
            logs.GetSnapshot(),
            record => record.Message.Contains("0 script(s) would be applied"));
        Assert.Equal(2, (await JournalAsync(connectionString)).Count);
    }

    private static async Task<object?> ScalarOrNullAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        using var cmd = new SqlCommand(sql, connection);
        var value = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        return value is DBNull ? null : value;
    }

    [Fact(Explicit = true)]
    public async Task TransitionPhase_AppliesItsScriptsWithoutJournalingThem()
    {
        var (initial, connectionString, _) = await BuildMigratorAsync("transition_test");
        await initial.MigrateAsync(TestContext.Current.CancellationToken);

        var (transition, _, _) = await BuildMigratorAsync(
            "transition_test",
            opts => opts.Phase = MigrationPhase.Transition);

        await transition.MigrateAsync(TestContext.Current.CancellationToken);
        await transition.MigrateAsync(TestContext.Current.CancellationToken);

        // The script ran — twice, and idempotently — and left no trace in the journal.
        Assert.Equal(["backfilled", "initial"], await OrderNamesAsync(connectionString));

        var journal = await JournalAsync(connectionString);
        Assert.Equal(2, journal.Count);
        Assert.DoesNotContain(journal, name => name.Contains(".Transition.", StringComparison.Ordinal));
    }

    [Fact(Explicit = true)]
    public async Task ASqlServerSchema_IsQueryableThroughEfCore_WhileDbUpOwnsTheSchema()
    {
        // SQL Server opts out of EF *migrations*, not EF. A service that reads the schema registers
        // with AddDatabase and gets a working DbContext; only the migrator is swapped for DbUp.
        var connectionString = await sqlServer.ConnectionStringForAsync("ef_query_test");

        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<DatabaseOptions>("Test", opts =>
        {
            opts.Provider = DatabaseProvider.SqlServer;
            opts.ConnectionString = connectionString;
        });
        services.Configure<SqlServerMigrationOptions>("Test", opts => opts.ScriptPrefix = ScriptPrefix);
        services.AddDatabase<OrdersDbContext, WidgetMigrationsAssembly>(
            "Test",
            static (opts, builder) => builder.UseSqlServer(opts.ConnectionString));

        await using var serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();

        // The keyed migrator resolves to DbUp even though a DbContext is registered.
        var migrator = scope.ServiceProvider.GetRequiredKeyedService<IDatabaseMigrator>("Test");
        await migrator.MigrateAsync(TestContext.Current.CancellationToken);

        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var names = await context.Orders
            .Select(order => order.Name)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["initial"], names);
    }

    [Fact(Explicit = true)]
    public async Task PreviousScriptNamespace_ThatTheCurrentOneExtends_IsAppliedOnce()
    {
        // Adoption by moving scripts into a provider subfolder, so the new prefix contains the old
        // one. An unanchored rewrite re-appended the new segment on every run until no journal row
        // matched a script any more and the whole set looked pending again.
        const string CurrentPrefix = $"{ScriptPrefix}.";
        const string PreviousPrefix = "Bitwarden.Server.Sdk.Database.Tests.Migrations";

        var (seed, connectionString, _) = await BuildMigratorAsync("rename_extend_test");
        await seed.MigrateAsync(TestContext.Current.CancellationToken);

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await new SqlCommand(
                $"UPDATE [dbo].[Migration] SET [ScriptName] = "
                + $"STUFF([ScriptName], 1, LEN('{CurrentPrefix}'), '{PreviousPrefix}.') "
                + $"WHERE LEFT([ScriptName], LEN('{CurrentPrefix}')) = '{CurrentPrefix}'",
                connection).ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var (adopting, _, _) = await BuildMigratorAsync(
            "rename_extend_test",
            opts => opts.PreviousScriptPrefix = PreviousPrefix);

        await adopting.MigrateAsync(TestContext.Current.CancellationToken);
        var afterFirst = await JournalAsync(connectionString);

        await adopting.MigrateAsync(TestContext.Current.CancellationToken);
        var afterSecond = await JournalAsync(connectionString);

        // The second run changes nothing, and no script was re-applied.
        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(2, afterSecond.Count);
        Assert.All(afterSecond, name => Assert.StartsWith(CurrentPrefix, name));
        Assert.Equal(["initial"], await OrderNamesAsync(connectionString));
    }

    [Fact(Explicit = true)]
    public async Task PreviousScriptNamespace_AdoptsAJournalWrittenUnderAnOlderName()
    {
        // A journal from an era whose names this build no longer produces: without the rewrite every
        // script would look pending and re-run.
        var (seed, connectionString, _) = await BuildMigratorAsync("rename_test");
        await seed.MigrateAsync(TestContext.Current.CancellationToken);

        // The recorded name is everything ahead of the script folder, so the whole prefix has to be
        // swapped — leaving part of it behind would rewrite onto a name that still doesn't match.
        const string currentPrefix = $"{ScriptPrefix}.";

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await new SqlCommand(
                $"UPDATE [dbo].[Migration] SET [ScriptName] = "
                + $"REPLACE([ScriptName], '{currentPrefix}', 'Bit.Setup.')",
                connection).ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        Assert.All(await JournalAsync(connectionString), name => Assert.StartsWith("Bit.Setup.", name));

        var (adopting, _, _) = await BuildMigratorAsync(
            "rename_test",
            opts => opts.PreviousScriptPrefix = "Bit.Setup");

        await adopting.MigrateAsync(TestContext.Current.CancellationToken);

        // Rewritten in place, and recognised as applied rather than re-run.
        var journal = await JournalAsync(connectionString);
        Assert.Equal(2, journal.Count);
        Assert.All(journal, name => Assert.StartsWith(currentPrefix, name));
        Assert.Equal(["initial"], await OrderNamesAsync(connectionString));
    }
}
