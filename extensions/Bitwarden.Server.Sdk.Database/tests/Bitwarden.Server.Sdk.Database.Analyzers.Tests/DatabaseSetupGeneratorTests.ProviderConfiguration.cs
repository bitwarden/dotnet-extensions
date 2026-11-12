using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Database.Analyzers.Tests;

/// <summary>
/// The generated <c>Add{Schema}</c> overload that takes the caller's provider configuration in place
/// of the generated one.
/// </summary>
public partial class DatabaseSetupGeneratorTests
{
    /// <summary>
    /// One migration per EF Core engine, each with an ID of its own, so a context that sees another
    /// engine's migration is caught by its ID.
    /// </summary>
    [StringSyntax("C#")]
    private const string SchemaWithAMigrationPerEngine =
        """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Migrations;
        using Bitwarden.Server.Sdk.Database;
        using My.App;

        [assembly: DatabaseSetup<MyDatabaseContext>([DatabaseProvider.Sqlite, DatabaseProvider.SqlServer, DatabaseProvider.PostgreSql, DatabaseProvider.MySql], MigratorKey = "Vault")]

        namespace My.App
        {
            public class MyDatabaseContext(DbContextOptions options) : DbContext(options);
        }

        namespace My.App.Migrations.Sqlite
        {
            [Migration("20260101000000_SqliteWidgets")]
            public class SqliteWidgets : Migration
            {
                protected override void Up(MigrationBuilder migrationBuilder)
                    => migrationBuilder.Sql("CREATE TABLE Widgets (Id INTEGER PRIMARY KEY);");
            }
        }

        namespace My.App.Migrations.PostgreSql
        {
            [Migration("20260101000000_PostgreSqlWidgets")]
            public class PostgreSqlWidgets : Migration
            {
                protected override void Up(MigrationBuilder migrationBuilder)
                    => migrationBuilder.Sql("CREATE TABLE widgets (id integer PRIMARY KEY);");
            }
        }

        namespace My.App.Migrations.MySql
        {
            [Migration("20260101000000_MySqlWidgets")]
            public class MySqlWidgets : Migration
            {
                protected override void Up(MigrationBuilder migrationBuilder)
                    => migrationBuilder.Sql("CREATE TABLE Widgets (Id INT PRIMARY KEY);");
            }
        }
        """;

    private const string UnreachableSqlServer =
        "Server=(local);Database=Nope;Trusted_Connection=True;TrustServerCertificate=True";

    [Fact]
    public async Task CallerProviderConfiguration_IsUsedToOpenTheContext()
    {
        var assembly = EmitAndLoad(RunGenerator(await BuildCompilationAsync(SchemaWithMigration)));

        var connectionString = $"Data Source={Guid.NewGuid()};Mode=Memory;Cache=Shared";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync(TestContext.Current.CancellationToken);

        DatabaseProvider? configuredFor = null;
        await using var provider = BuildProviderWith(
            assembly,
            DatabaseProvider.Sqlite,
            connectionString,
            (options, builder) =>
            {
                configuredFor = options.Provider;
                builder.UseSqlite(options.ConnectionString, sqlite => sqlite.CommandTimeout(123));
            });
        await using var scope = provider.CreateAsyncScope();

        var context = (DbContext)scope.ServiceProvider.GetRequiredService(assembly.GetType("My.App.MyDatabaseContext")!);

        // A provider option only the caller's configuration sets.
        Assert.Equal(123, context.Database.GetCommandTimeout());
        Assert.Equal(DatabaseProvider.Sqlite, configuredFor);

        // ...and the migrator, through the same context, still applies the schema's migrations.
        await scope.ServiceProvider
            .GetRequiredKeyedService<IDatabaseMigrator>("Vault")
            .MigrateAsync(TestContext.Current.CancellationToken);

        using var cmd = keepAlive.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='Widgets'";

        Assert.Equal(1L, await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    public static TheoryData<string, string> EngineMigrations => new()
    {
        { "Sqlite", "20260101000000_SqliteWidgets" },
        { "PostgreSql", "20260101000000_PostgreSqlWidgets" },
        { "MySql", "20260101000000_MySqlWidgets" },
        { "MariaDb", "20260101000000_MySqlWidgets" },
    };

    [Theory]
    [MemberData(nameof(EngineMigrations))]
    public async Task CallerProviderConfiguration_EachEngineStillGetsOnlyItsOwnMigrations(
        string engine, string migrationId)
    {
        // Reading the migration list touches the dispatcher but not the database, so no server is
        // needed. MySQL and MariaDB are described by an explicit server version, as a caller that
        // caches or fixes it would, so nothing asks a server for one. MariaDB, configured as itself,
        // gets MySQL's migration and no other.
        var assembly = EmitAndLoad(RunGenerator(await BuildCompilationAsync(SchemaWithAMigrationPerEngine)));

        var (databaseProvider, connectionString, configure) = engine switch
        {
            "Sqlite" => (DatabaseProvider.Sqlite, "Data Source=:memory:",
                (Action<DatabaseOptions, DbContextOptionsBuilder>)((o, b) => b.UseSqlite(o.ConnectionString))),
            "PostgreSql" => (DatabaseProvider.PostgreSql, "Host=localhost;Database=nope",
                (o, b) => b.UseNpgsql(o.ConnectionString)),
            "MySql" => (DatabaseProvider.MySql, "Server=localhost;Database=nope",
                (o, b) => b.UseMySql(o.ConnectionString, new MySqlServerVersion(new Version(8, 4)))),
            "MariaDb" => (DatabaseProvider.MariaDb, "Server=localhost;Database=nope",
                (o, b) => b.UseMySql(o.ConnectionString, new MariaDbServerVersion(new Version(11, 4)))),
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null),
        };

        await using var provider = BuildProviderWith(assembly, databaseProvider, connectionString, configure);
        await using var scope = provider.CreateAsyncScope();

        var context = (DbContext)scope.ServiceProvider.GetRequiredService(assembly.GetType("My.App.MyDatabaseContext")!);

        Assert.Equal([migrationId], context.Database.GetMigrations());
    }

    [Fact]
    public async Task CallerProviderConfiguration_SqlServerIsStillRefusedToEfCore()
    {
        var assembly = EmitAndLoad(RunGenerator(await BuildCompilationAsync(SchemaWithAMigrationPerEngine)));

        await using var provider = BuildProviderWith(
            assembly,
            DatabaseProvider.SqlServer,
            UnreachableSqlServer,
            (options, builder) => builder.UseSqlServer(options.ConnectionString));
        await using var scope = provider.CreateAsyncScope();

        Assert.Equal(
            "SqlServerMigrator",
            scope.ServiceProvider.GetRequiredKeyedService<IDatabaseMigrator>("Vault").GetType().Name);

        var context = (DbContext)scope.ServiceProvider.GetRequiredService(assembly.GetType("My.App.MyDatabaseContext")!);
        var error = Record.Exception(() => context.Database.GetMigrations());

        Assert.NotNull(error);
        Assert.Contains("migrates SQL Server with DbUp, not EF Core migrations", error.ToString());
    }

    [Fact]
    public async Task CallerProviderConfiguration_CannotReplaceTheGeneratedMigrationsAssembly()
    {
        // The generated migrations assembly is installed after the caller's configuration runs, so a
        // caller that installs its own, as a platform's existing registration may, doesn't displace it.
        var assembly = EmitAndLoad(RunGenerator(await BuildCompilationAsync(SchemaWithAMigrationPerEngine)));

        await using var provider = BuildProviderWith(
            assembly,
            DatabaseProvider.Sqlite,
            "Data Source=:memory:",
            (options, builder) => builder
                .UseSqlite(options.ConnectionString)
                .ReplaceService<IMigrationsAssembly, NoMigrations>());
        await using var scope = provider.CreateAsyncScope();

        var context = (DbContext)scope.ServiceProvider.GetRequiredService(assembly.GetType("My.App.MyDatabaseContext")!);

        Assert.Equal(["20260101000000_SqliteWidgets"], context.Database.GetMigrations());
    }

    [Fact]
    public async Task CallerProviderConfiguration_EfCoreSqlServer_KeepsEfCoreMigrations()
    {
        var assembly = EmitAndLoad(RunGenerator(await BuildCompilationAsync(EfCoreSqlServerSchema)));

        await using var provider = BuildProviderWith(
            assembly,
            DatabaseProvider.SqlServer,
            UnreachableSqlServer,
            (options, builder) => builder.UseSqlServer(options.ConnectionString));
        await using var scope = provider.CreateAsyncScope();

        var migrator = scope.ServiceProvider.GetRequiredKeyedService<IDatabaseMigrator>("Vault");
        Assert.StartsWith("EfCoreMigrator", migrator.GetType().Name);

        var context = (DbContext)scope.ServiceProvider.GetRequiredService(assembly.GetType("My.App.MyDatabaseContext")!);
        Assert.Equal(["20260101000000_CreateWidgets"], context.Database.GetMigrations());
    }

    [Fact]
    public async Task CallerProviderConfiguration_Null_Throws()
    {
        var assembly = EmitAndLoad(RunGenerator(await BuildCompilationAsync(SchemaWithMigration)));

        var error = Assert.Throws<TargetInvocationException>(
            () => AddWithProviderConfiguration(assembly, new ServiceCollection(), null));

        var argument = Assert.IsType<ArgumentNullException>(error.InnerException);
        Assert.Equal("configureProvider", argument.ParamName);
    }

    [Fact]
    public async Task CallerProviderConfiguration_IsNotOfferedByTheMigratorBuild()
    {
        // The migrator build registers no context, so there is no provider to configure.
        var parseOptions = _parseOptions.WithPreprocessorSymbols("SQLSERVER_MIGRATOR_BUILD");
        var compilation = await BuildCompilationAsync(SchemaWithMigration, parseOptions: parseOptions);

        var extensions = RunGenerator(compilation, parseOptions)
            .GetTypeByMetadataName("My.App.MyDatabaseServiceCollectionExtensions");

        Assert.NotNull(extensions);
        Assert.Single(extensions.GetMembers("AddMyDatabase"));
    }

    /// <summary>
    /// Registers the generated schema with the caller's provider configuration, as a consuming host would.
    /// </summary>
    private static ServiceProvider BuildProviderWith(
        Assembly generated,
        DatabaseProvider databaseProvider,
        string connectionString,
        Action<DatabaseOptions, DbContextOptionsBuilder> configureProvider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<DatabaseOptions>("Vault", opts =>
        {
            opts.Provider = databaseProvider;
            opts.ConnectionString = connectionString;
        });

        AddWithProviderConfiguration(generated, services, configureProvider);

        return services.BuildServiceProvider();
    }

    private static void AddWithProviderConfiguration(
        Assembly generated,
        IServiceCollection services,
        Action<DatabaseOptions, DbContextOptionsBuilder>? configureProvider)
    {
        var extensions = generated.GetType("My.App.MyDatabaseServiceCollectionExtensions");
        Assert.NotNull(extensions);

        var addWithProviderConfiguration = extensions.GetMethod(
            "AddMyDatabase",
            [typeof(IServiceCollection), typeof(Action<DatabaseOptions, DbContextOptionsBuilder>)]);
        Assert.NotNull(addWithProviderConfiguration);

        addWithProviderConfiguration.Invoke(null, [services, configureProvider]);
    }

    /// <summary>
    /// A migrations assembly with no migrations, which a caller's configuration tries to install.
    /// </summary>
    private sealed class NoMigrations : IMigrationsAssembly
    {
        public IReadOnlyDictionary<string, TypeInfo> Migrations { get; } = new Dictionary<string, TypeInfo>();

        public ModelSnapshot? ModelSnapshot => null;

        public Assembly Assembly => typeof(NoMigrations).Assembly;

        public string? FindMigrationId(string nameOrId) => null;

        public Migration CreateMigration(TypeInfo migrationClass, string activeProvider)
            => throw new InvalidOperationException("No migrations.");
    }
}
