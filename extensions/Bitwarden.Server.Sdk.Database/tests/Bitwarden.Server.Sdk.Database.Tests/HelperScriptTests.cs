using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Database.Tests;

public class HelperScriptTests
{
    private const string Sqlite = "Microsoft.EntityFrameworkCore.Sqlite";
    private const string MySql = "Pomelo.EntityFrameworkCore.MySql";

    private static readonly string[] PerProvider =
    [
        "Bit.OrderDatabase.Migrations.Sqlite.HelperScripts.2026-08-19_00_Backfill.sql",
        "Bit.OrderDatabase.Migrations.MySql.HelperScripts.2026-08-19_00_Backfill.sql",
        "Bit.OrderDatabase.SqlServer.2026-08-12_00_CreateOrders.sql",
    ];

    private static string Resolve(IReadOnlyCollection<string> names, string? provider, string file)
        => MigrationBuilderExtensions.ResolveHelperScript(names, provider, file);

    [Fact]
    public async Task Migration_RunsTheScriptForItsProvider()
    {
        // End to end: EF applies the migration, the migration asks for a script by file name, and
        // the SQLite copy runs — not the MySQL one sharing that name. The value in the row is what
        // distinguishes the two.
        await using var fixture = SqliteTestDatabase.Create<TestDbContext, WidgetMigrationsAssembly>();

        using (var scope = fixture.ServiceProvider.CreateScope())
        {
            var migrator = scope.ServiceProvider.GetRequiredKeyedService<IDatabaseMigrator>("Test");
            await migrator.MigrateAsync(TestContext.Current.CancellationToken);
        }

        using var cmd = fixture.KeepAlive.CreateCommand();
        cmd.CommandText = "SELECT Name FROM Widgets WHERE Id = 1";

        Assert.Equal(
            "from-sqlite-helper-script",
            await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SameFileName_ResolvesPerProvider()
    {
        // The point of the provider folder: one call in a migration picks the right dialect.
        Assert.Equal(PerProvider[0], Resolve(PerProvider, Sqlite, "2026-08-19_00_Backfill.sql"));
        Assert.Equal(PerProvider[1], Resolve(PerProvider, MySql, "2026-08-19_00_Backfill.sql"));
    }

    [Fact]
    public void SingleScript_IsUsedForAnyProvider()
    {
        // A script that works on every provider needs only one copy.
        string[] shared = ["Bit.OrderDatabase.Migrations.HelperScripts.Shared.sql"];

        Assert.Equal(shared[0], Resolve(shared, Sqlite, "Shared.sql"));
        Assert.Equal(shared[0], Resolve(shared, MySql, "Shared.sql"));
    }

    [Fact]
    public void MigrationScripts_AreNotHelperScripts()
    {
        // A DbUp script has no HelperScripts segment, so it can never be picked up here.
        var error = Assert.Throws<InvalidOperationException>(
            () => Resolve(PerProvider, Sqlite, "2026-08-12_00_CreateOrders.sql"));

        Assert.Contains("No helper script named", error.Message);
    }

    [Fact]
    public void MissingScript_NamesWhatIsEmbedded()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Resolve(PerProvider, Sqlite, "Nope.sql"));

        Assert.Contains("2026-08-19_00_Backfill.sql", error.Message);
        Assert.Contains("Migrations/{Provider}/HelperScripts/", error.Message);
    }

    [Fact]
    public void NoHelperScriptsAtAll_SaysSo()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Resolve(["Bit.OrderDatabase.SqlServer.2026-08-12_00_CreateOrders.sql"], Sqlite, "Nope.sql"));

        Assert.Contains("embeds no helper scripts at all", error.Message);
    }

    [Fact]
    public void AmbiguousScript_ForAnUnknownProvider_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Resolve(PerProvider, "Contoso.EntityFrameworkCore.Widgets", "2026-08-19_00_Backfill.sql"));

        Assert.Contains("none is under a folder for provider", error.Message);
    }
}
