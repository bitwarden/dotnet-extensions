using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Database.Tests;

/// <summary>Minimal DbContext used in tests. Contains no entities or migrations.</summary>
internal class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);

/// <summary>Second minimal DbContext for multi-schema tests.</summary>
internal class AnotherTestDbContext(DbContextOptions<AnotherTestDbContext> options) : DbContext(options);

/// <summary>
/// Wraps a <see cref="ServiceProvider"/> and the <see cref="SqliteConnection"/> keep-alive
/// that in-memory SQLite needs, disposing both together.
/// </summary>
internal sealed class DatabaseFixture : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly SqliteConnection _keepAlive;

    public DatabaseFixture(ServiceProvider serviceProvider, SqliteConnection keepAlive)
    {
        _serviceProvider = serviceProvider;
        _keepAlive = keepAlive;
    }

    public ServiceProvider ServiceProvider => _serviceProvider;
    public SqliteConnection KeepAlive => _keepAlive;

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }
}

/// <summary>
/// IMigrationsAssembly implementation with no migrations, for testing AddDatabase
/// without requiring a generated or hand-written migrations assembly.
/// </summary>
internal sealed class EmptyMigrationsAssembly : IMigrationsAssembly
{
    // EF Core resolves IMigrationsAssembly from its internal DI; the options parameter
    // is required by the injection contract but is not needed for an empty implementation.
    public EmptyMigrationsAssembly(IDbContextOptions options) { }

    public IReadOnlyDictionary<string, TypeInfo> Migrations { get; } =
        new Dictionary<string, TypeInfo>().AsReadOnly();

    public ModelSnapshot? ModelSnapshot => null;

    public Assembly Assembly => typeof(EmptyMigrationsAssembly).Assembly;

    public string? FindMigrationId(string nameOrId) => null;

    public Migration CreateMigration(TypeInfo migrationClass, string activeProvider) =>
        throw new NotSupportedException("No migrations defined in test assembly.");
}

/// <summary>
/// Registers a Sqlite-backed schema, standing in for the generated <c>Add{Schema}Database</c>
/// extension that normally supplies the provider callback.
/// </summary>
internal static class TestDatabaseExtensions
{
    public static IServiceCollection AddSqliteTestDatabase<TContext>(
        this IServiceCollection services,
        string name)
        where TContext : DbContext
        => services.AddSqliteTestDatabase<TContext, EmptyMigrationsAssembly>(name);

    public static IServiceCollection AddSqliteTestDatabase<TContext, TMigrationsAssembly>(
        this IServiceCollection services,
        string name)
        where TContext : DbContext
        where TMigrationsAssembly : class, IMigrationsAssembly
        => services.AddDatabase<TContext, TMigrationsAssembly>(
            name,
            static (opts, builder) => builder.UseSqlite(opts.ConnectionString));
}

/// <summary>Builds a Sqlite-backed provider for a schema, with the connection kept alive.</summary>
internal static class SqliteTestDatabase
{
    public static DatabaseFixture Create<TContext, TMigrationsAssembly>(
        string name = "Test",
        Action<IServiceCollection>? configure = null)
        where TContext : DbContext
        where TMigrationsAssembly : class, IMigrationsAssembly
    {
        var connectionString = $"Data Source={Guid.NewGuid()};Mode=Memory;Cache=Shared";

        var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<DatabaseOptions>(name, opts =>
        {
            opts.Provider = DatabaseProvider.Sqlite;
            opts.ConnectionString = connectionString;
        });
        services.AddSqliteTestDatabase<TContext, TMigrationsAssembly>(name);

        // Runs after registration, so a test can override what the schema registered.
        configure?.Invoke(services);

        return new DatabaseFixture(services.BuildServiceProvider(), keepAlive);
    }
}

/// <summary>Creates a <c>Widgets</c> table, so a migration run has observable effects.</summary>
[Migration(CreateWidgets.MigrationId)]
internal sealed class CreateWidgets : Migration
{
    public const string MigrationId = "20260101000000_CreateWidgets";

    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.CreateTable(
            name: "Widgets",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false),
                Name = table.Column<string>(nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_Widgets", x => x.Id));

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable("Widgets");
}

/// <summary>Runs a helper script, so a migration's effect comes from a <c>.sql</c> file.</summary>
[Migration(SeedWidgets.MigrationId)]
internal sealed class SeedWidgets : Migration
{
    public const string MigrationId = "20260102000000_SeedWidgets";

    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.SqlFromHelperScript("2026-08-19_00_SeedWidget.sql");

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("DELETE FROM Widgets WHERE Id = 1;");
}

/// <summary>
/// Hand-written stand-in for a generated migrations-assembly dispatcher, wired the same way:
/// one static entry per migration, each with a factory instead of reflection.
/// </summary>
internal sealed class WidgetMigrationsAssembly : MigrationsAssemblyBase
{
    private static readonly (string Id, TypeInfo Info, Func<Migration> Factory)[] Entries =
    [
        (CreateWidgets.MigrationId, typeof(CreateWidgets).GetTypeInfo(), static () => new CreateWidgets()),
        (SeedWidgets.MigrationId, typeof(SeedWidgets).GetTypeInfo(), static () => new SeedWidgets()),
    ];

    // Takes IDatabaseProvider like the generated dispatcher does, so these tests fail if EF can't
    // inject it into a replaced IMigrationsAssembly.
    public WidgetMigrationsAssembly(IDatabaseProvider provider)
        : base(typeof(WidgetMigrationsAssembly).Assembly, Entries, modelSnapshot: null)
    {
        // Injection is the point; which provider it is varies by test.
        Assert.NotEmpty(provider.Name);
    }
}
