using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Database.Analyzers.Tests;

/// <summary>
/// SQL Server migrated by EF Core (<c>SqlServerMigrations = SqlServerMigrations.EfCore</c>), and
/// contexts whose constructors take more than their options.
/// </summary>
public partial class DatabaseSetupGeneratorTests
{
    [StringSyntax("C#")]
    private const string EfCoreSqlServerSchema =
        """
        using Microsoft.EntityFrameworkCore;
        using Bitwarden.Server.Sdk.Database;
        using My.App;

        [assembly: DatabaseSetup<MyDatabaseContext>(
            [DatabaseProvider.SqlServer, DatabaseProvider.PostgreSql, DatabaseProvider.MySql],
            MigratorKey = "Vault",
            SqlServerMigrations = SqlServerMigrations.EfCore)]

        namespace My.App
        {
            public class MyDatabaseContext(DbContextOptions options) : DbContext(options);
        }

        namespace My.App.Migrations.SqlServer
        {
            using Microsoft.EntityFrameworkCore.Migrations;

            [Migration("20260101000000_CreateWidgets")]
            public class CreateWidgets : Migration
            {
                protected override void Up(MigrationBuilder migrationBuilder)
                    => migrationBuilder.Sql("CREATE TABLE [Widgets] ([Id] INT NOT NULL PRIMARY KEY);");

                protected override void Down(MigrationBuilder migrationBuilder)
                    => migrationBuilder.Sql("DROP TABLE [Widgets];");
            }
        }
        """;

    [StringSyntax("C#")]
    private const string ForwardedConstructorSchema =
        """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.DependencyInjection;
        using Bitwarden.Server.Sdk.Database;
        using My.App;

        [assembly: DatabaseSetup<MyDatabaseContext>(
            [DatabaseProvider.Sqlite, DatabaseProvider.SqlServer],
            MigratorKey = "Vault",
            SqlServerMigrations = SqlServerMigrations.EfCore,
            DesignTimeServices = typeof(MyDesignTimeServices))]

        namespace My.App;

        public interface IRowOwnership;

        public sealed class NoOwnership : IRowOwnership;

        public sealed class MyDesignTimeServices : IDatabaseDesignTimeServices
        {
            public void ConfigureServices(IServiceCollection services)
                => services.AddSingleton<IRowOwnership, NoOwnership>();
        }

        public class MyDatabaseContext(DbContextOptions<MyDatabaseContext> options, IRowOwnership ownership, int retries = 3)
            : DbContext(options)
        {
            public IRowOwnership Ownership { get; } = ownership;

            public int Retries { get; } = retries;
        }
        """;

    [Fact]
    public async Task EfCoreSqlServer_IsScaffoldedAndDispatchedLikeTheOtherEngines()
    {
        var generated = await GenerateAsync(EfCoreSqlServerSchema);

        Assert.Contains(
            "internal sealed class SqlServerContext(DbContextOptions<SqlServerContext> o) : MyDatabaseContext(o);",
            generated);
        Assert.Contains("file sealed class SqlServerContextFactory : IDesignTimeDbContextFactory<SqlServerContext>", generated);
        Assert.Contains(
            "if (provider == \"Microsoft.EntityFrameworkCore.SqlServer\") return _sqlServerEntries;", generated);
        Assert.Contains("(\"My.App.SqlServerContext\", \"Migrations/SqlServer\", \"My.App.Migrations.SqlServer\"),", generated);
        Assert.Contains("SqlServerMigrations.EfCore);", generated);

        // No scripts, so no DbUp: no script prefix, no DbUp-only migrator variant.
        Assert.DoesNotContain("migrates SQL Server with DbUp", generated);
        Assert.DoesNotContain("ScriptPrefix", generated);
        Assert.DoesNotContain("AddSqlServerDatabaseMigrator", generated);
        Assert.DoesNotContain("RunSqlServerAsync", generated);
    }

    [Fact]
    public async Task EfCoreSqlServer_RefusesTheMigratorBuild()
    {
        var parseOptions = _parseOptions.WithPreprocessorSymbols("SQLSERVER_MIGRATOR_BUILD");
        var compilation = await BuildCompilationAsync(EfCoreSqlServerSchema, parseOptions: parseOptions);

        var errors = RunGenerator(compilation, parseOptions)
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        // Our #error, which the errors from the code the symbol strips then follow.
        Assert.Contains(errors, error =>
            error.Id == "CS1029"
            && error.GetMessage().Contains("migrates SQL Server with EF Core migrations, so it has no SQLSERVER_MIGRATOR_BUILD"));
    }

    [Fact]
    public async Task EfCoreSqlServer_RegistersAnEfCoreMigratorAndHandsOutItsMigrations()
    {
        // Reading the migration list touches the dispatcher but not the database, so no server is needed.
        var assembly = EmitAndLoad(RunGenerator(await BuildCompilationAsync(EfCoreSqlServerSchema)));

        await using var provider = BuildProviderFor(
            assembly,
            DatabaseProvider.SqlServer,
            "Server=(local);Database=Nope;Trusted_Connection=True;TrustServerCertificate=True");
        await using var scope = provider.CreateAsyncScope();

        var migrator = scope.ServiceProvider.GetRequiredKeyedService<IDatabaseMigrator>("Vault");
        Assert.StartsWith("EfCoreMigrator", migrator.GetType().Name);

        var context = (DbContext)scope.ServiceProvider.GetRequiredService(assembly.GetType("My.App.MyDatabaseContext")!);
        Assert.Equal(["20260101000000_CreateWidgets"], context.Database.GetMigrations());
    }

    [Fact]
    public async Task DbUpSqlServer_IsStillTheDefault()
    {
        var assembly = EmitAndLoad(RunGenerator(await BuildCompilationAsync(SchemaWithMigration)));

        await using var provider = BuildProviderFor(
            assembly,
            DatabaseProvider.SqlServer,
            "Server=(local);Database=Nope;Trusted_Connection=True;TrustServerCertificate=True");
        await using var scope = provider.CreateAsyncScope();

        Assert.Equal("SqlServerMigrator", scope.ServiceProvider.GetRequiredKeyedService<IDatabaseMigrator>("Vault").GetType().Name);
    }

    [Fact(Explicit = true)]
    public async Task EfCoreSqlServer_AppliesItsMigrationsToSqlServer()
    {
        // The generated CLI, given a SQL Server connection string, applying an EF Core migration and
        // recording it in EF's history rather than DbUp's journal.
        var assembly = EmitAndLoad(RunGenerator(await BuildCompilationAsync(EfCoreSqlServerSchema)));
        var connectionString = await _sqlServer.ConnectionStringForAsync("generated_ef_sql_server_test");

        var output = new StringWriter();
        var exitCode = await InvokeCliAsync(assembly, output, "--sqlserver", connectionString);
        Assert.True(exitCode == 0, $"CLI exited {exitCode}: {output}");

        Assert.True(await TableExistsAsync(connectionString, "Widgets"));
        Assert.Equal("20260101000000_CreateWidgets", await ScalarAsync(
            connectionString, "SELECT TOP 1 [MigrationId] FROM [dbo].[__EFMigrationsHistory]"));
        Assert.False(await TableExistsAsync(connectionString, "Migration"));

        // Migrating again finds nothing pending.
        Assert.Equal(0, await InvokeCliAsync(assembly, "--sqlserver", connectionString));
    }

    [Fact]
    public async Task ContextConstructor_IsForwardedToEachSubcontext()
    {
        var generated = await GenerateAsync(ForwardedConstructorSchema);

        Assert.Contains(
            "internal sealed class SqliteContext(global::Microsoft.EntityFrameworkCore.DbContextOptions<global::My.App.MyDatabaseContext> @options, "
            + "global::My.App.IRowOwnership @ownership, int @retries = 3) : MyDatabaseContext(@options, @ownership, @retries);",
            generated);
        Assert.Contains(
            "return MyDatabaseDesignTimeActivator.Create<SqlServerContext>(new DbContextOptionsBuilder<MyDatabaseContext>().UseSqlServer(connectionString).Options);",
            generated);
        Assert.Contains("new global::My.App.MyDesignTimeServices().ConfigureServices(services);", generated);
    }

    [Fact]
    public async Task ForwardedConstructor_DesignTimeFactoryResolvesItsParameters()
    {
        // What `dotnet ef` calls, for a context that takes more than its options.
        var assembly = EmitAndLoad(RunGenerator(await BuildCompilationAsync(ForwardedConstructorSchema)));

        var factoryType = Assert.Single(
            assembly.GetTypes(),
            type => type.GetInterfaces().Any(i =>
                i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(IDesignTimeDbContextFactory<>)
                && i.GenericTypeArguments[0].Name == "SqliteContext"));

        var connectionString = $"Data Source={Guid.NewGuid()};Mode=Memory;Cache=Shared";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync(TestContext.Current.CancellationToken);

        using var context = (DbContext)factoryType.GetMethod("CreateDbContext")!.Invoke(
            Activator.CreateInstance(factoryType),
            [new[] { $"--ConnectionStrings:Sqlite={connectionString}" }])!;

        Assert.Equal("SqliteContext", context.GetType().Name);
        Assert.Equal("NoOwnership", context.GetType().GetProperty("Ownership")!.GetValue(context)!.GetType().Name);
        Assert.Equal(3, context.GetType().GetProperty("Retries")!.GetValue(context));
        Assert.Equal(connectionString, context.Database.GetConnectionString());
    }

    [Fact]
    public async Task ConstructorWithOnlyDefaultedExtras_NeedsNoDesignTimeServices()
    {
        var generated = await GenerateAsync(
            """
            using Microsoft.EntityFrameworkCore;
            using Bitwarden.Server.Sdk.Database;
            using My.App;

            [assembly: DatabaseSetup<MyDatabaseContext>([DatabaseProvider.Sqlite])]

            namespace My.App;

            public enum Mode { Off, On }

            public class MyDatabaseContext(DbContextOptions options, Mode mode = Mode.On, string? label = null, float weight = 1.5f)
                : DbContext(options);
            """);

        Assert.Contains(
            "internal sealed class SqliteContext(DbContextOptions<SqliteContext> @options, global::My.App.Mode @mode = (global::My.App.Mode)1, "
            + "string? @label = default, float @weight = 1.5F) : MyDatabaseContext(@options, @mode, @label, @weight);",
            generated);
        Assert.DoesNotContain("ConfigureServices", generated);
    }

    [Theory]
    [InlineData(
        "public sealed class MyDatabaseContext(DbContextOptions options) : DbContext(options);",
        "",
        "BWDB004")]
    [InlineData(
        "public class MyDatabaseContext(DbContextOptions options, IServiceProvider services) : DbContext(options);",
        "",
        "BWDB008")]
    [InlineData(
        "public class MyDatabaseContext : DbContext { private MyDatabaseContext(DbContextOptions options) : base(options) { } }",
        "",
        "BWDB005")]
    [InlineData(
        "public class MyDatabaseContext(DbContextOptions options) : DbContext(options); public class NotServices;",
        ", DesignTimeServices = typeof(NotServices)",
        "BWDB006")]
    public async Task UnusableContextShape_IsRejectedAtBuildTime(string context, string arguments, string id)
    {
        var diagnostics = await GenerateDiagnosticsAsync(
            $$"""
            using System;
            using Microsoft.EntityFrameworkCore;
            using Bitwarden.Server.Sdk.Database;
            using My.App;

            [assembly: DatabaseSetup<MyDatabaseContext>([DatabaseProvider.Sqlite]{{arguments}})]

            namespace My.App;

            {{context}}
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public async Task SealedContextMigratedOnlyByDbUp_IsAccepted()
    {
        // Without an EF Core engine there are no subcontexts, so sealed is fine.
        var diagnostics = await GenerateDiagnosticsAsync(
            """
            using Microsoft.EntityFrameworkCore;
            using Bitwarden.Server.Sdk.Database;
            using My.App;

            [assembly: DatabaseSetup<MyDatabaseContext>([DatabaseProvider.SqlServer])]

            namespace My.App;

            public sealed class MyDatabaseContext(DbContextOptions options) : DbContext(options);
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task EfCoreSqlServerWithoutSqlServer_Warns()
    {
        var diagnostics = await GenerateDiagnosticsAsync(
            """
            using Microsoft.EntityFrameworkCore;
            using Bitwarden.Server.Sdk.Database;
            using My.App;

            [assembly: DatabaseSetup<MyDatabaseContext>([DatabaseProvider.PostgreSql], SqlServerMigrations = SqlServerMigrations.EfCore)]

            namespace My.App;

            public class MyDatabaseContext(DbContextOptions options) : DbContext(options);
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("BWDB007", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }
}
