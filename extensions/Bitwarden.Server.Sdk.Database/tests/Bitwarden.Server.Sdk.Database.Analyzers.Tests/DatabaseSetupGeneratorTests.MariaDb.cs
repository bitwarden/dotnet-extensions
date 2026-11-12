using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure.Internal;
using Testcontainers.MariaDb;

namespace Bitwarden.Server.Sdk.Database.Analyzers.Tests;

/// <summary>
/// MariaDB, which a schema serves by declaring MySQL: the same EF Core provider and the same
/// migrations, configured as <see cref="DatabaseProvider.MariaDb"/>.
/// </summary>
public partial class DatabaseSetupGeneratorTests
{
    [Theory]
    [InlineData("DatabaseProvider.MariaDb")]
    [InlineData("DatabaseProvider.MySql, DatabaseProvider.MariaDb")]
    public async Task MariaDb_DeclaredOnTheSchema_IsRejectedAtBuildTime(string providers)
    {
        var diagnostics = await GenerateDiagnosticsAsync(
            $$"""
            using Microsoft.EntityFrameworkCore;
            using Bitwarden.Server.Sdk.Database;
            using My.App;

            [assembly: DatabaseSetup<MyDatabaseContext>([{{providers}}])]

            namespace My.App;

            public class MyDatabaseContext(DbContextOptions options) : DbContext(options);
            """);

        // Only BW0026: MariaDB has no package of its own, so it isn't also reported as missing one.
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("BW0026", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Declare DatabaseProvider.MySql", diagnostic.GetMessage());
    }

    [Fact]
    public async Task GeneratedCli_TakesAMariaDbOptionForAMySqlSchema()
    {
        var assembly = EmitAndLoad(RunGenerator(await BuildCompilationAsync(SchemaWithAMigrationPerEngine)));

        var output = new StringWriter();
        var exitCode = await InvokeCliAsync(assembly, output, "--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("--mysql", output.ToString());
        Assert.Contains("--mariadb", output.ToString());
        Assert.Contains("MariaDB connection string", output.ToString());
    }

    [Fact(Explicit = true)]
    public async Task GeneratedCliAndRegistration_MigrateARealMariaDbWithMySqlsMigrations()
    {
        // Against a real server: the generated executable migrates MariaDB through --mariadb, and the
        // generated registration, configured for MariaDb, opens it with the version the server reports.
        await using var container = new MariaDbBuilder().WithImage("mariadb:11.4").Build();
        await container.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = container.GetConnectionString();

        var assembly = EmitAndLoad(RunGenerator(await BuildCompilationAsync(SchemaWithAMigrationPerEngine)));

        var output = new StringWriter();
        var exitCode = await InvokeCliAsync(assembly, output, "--mariadb", connectionString);
        Assert.True(exitCode == 0, $"Migrate exited {exitCode}: {output}");
        Assert.Contains("Starting MariaDb migrations", output.ToString());

        await using var provider = BuildProviderFor(assembly, DatabaseProvider.MariaDb, connectionString);
        await using var scope = provider.CreateAsyncScope();
        var context = (DbContext)scope.ServiceProvider.GetRequiredService(assembly.GetType("My.App.MyDatabaseContext")!);

        Assert.Equal(
            ["20260101000000_MySqlWidgets"],
            await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));

        // The provider exposes the version the context was opened with only through its options
        // extension, an internal API; a test may lean on it.
#pragma warning disable EF1001
        var serverVersion = context.GetService<IDbContextOptions>().FindExtension<MySqlOptionsExtension>()!.ServerVersion;
#pragma warning restore EF1001
        Assert.Equal(ServerType.MariaDb, serverVersion.Type);
    }
}
