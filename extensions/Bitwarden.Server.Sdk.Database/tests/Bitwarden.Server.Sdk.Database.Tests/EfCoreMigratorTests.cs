using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Database.Tests;

public class EfCoreMigratorTests
{
    private static DatabaseFixture BuildFixture()
        => SqliteTestDatabase.Create<TestDbContext, EmptyMigrationsAssembly>();

    private static DatabaseFixture BuildMigratingFixture()
        => SqliteTestDatabase.Create<TestDbContext, WidgetMigrationsAssembly>();

    private static async Task MigrateAsync(DatabaseFixture fixture)
    {
        using var scope = fixture.ServiceProvider.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredKeyedService<IDatabaseMigrator>("Test");
        await migrator.MigrateAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> ScalarAsync(DatabaseFixture fixture, string sql)
    {
        using var cmd = fixture.KeepAlive.CreateCommand();
        cmd.CommandText = sql;
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static Task<long> CountWidgetTablesAsync(DatabaseFixture fixture)
        => ScalarAsync(fixture, "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='Widgets'");

    private static Task<long> CountHistoryRowsAsync(DatabaseFixture fixture)
        => ScalarAsync(
            fixture,
            $"SELECT COUNT(1) FROM __EFMigrationsHistory WHERE MigrationId = '{CreateWidgets.MigrationId}'");

    [Fact]
    public async Task MigrateAsync_WithSqlite_Succeeds()
    {
        await using var fixture = BuildFixture();
        using var scope = fixture.ServiceProvider.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredKeyedService<IDatabaseMigrator>("Test");
        // Should complete without throwing (applies 0 migrations from EmptyMigrationsAssembly)
        await migrator.MigrateAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MigrateAsync_AppliesMigration_CreatesTableAndJournalsIt()
    {
        await using var fixture = BuildMigratingFixture();

        await MigrateAsync(fixture);

        Assert.Equal(1, await CountWidgetTablesAsync(fixture));
        Assert.Equal(1, await CountHistoryRowsAsync(fixture));
    }

    [Fact]
    public async Task MigrateAsync_AppliedMigration_SecondRunIsANoOp()
    {
        await using var fixture = BuildMigratingFixture();

        await MigrateAsync(fixture);
        await MigrateAsync(fixture);

        // A re-applied CreateTable would fail outright, so reaching here already proves the
        // journal was honoured; the row count confirms it wasn't recorded twice.
        Assert.Equal(1, await CountHistoryRowsAsync(fixture));
    }

}
