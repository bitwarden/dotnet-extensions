using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace Bitwarden.Server.Sdk.Database.Tests;

/// <summary>
/// The EF providers the SDK supports beyond SQLite, each applying the same migrations through the
/// dispatcher — including a helper script written in that provider's own dialect.
/// </summary>
public class EfProviderIntegrationTests
{
    private static async Task<string> MigrateAndReadWidgetAsync(
        string connectionString,
        Action<DatabaseOptions, DbContextOptionsBuilder> configureProvider,
        DatabaseProvider provider,
        string selectWidget)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<DatabaseOptions>("Test", opts =>
        {
            opts.Provider = provider;
            opts.ConnectionString = connectionString;
        });
        services.AddDatabase<TestDbContext, WidgetMigrationsAssembly>("Test", configureProvider);

        await using var serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredKeyedService<IDatabaseMigrator>("Test")
            .MigrateAsync(TestContext.Current.CancellationToken);

        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = selectWidget;

        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        return (string)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    [Fact(Explicit = true)]
    public async Task PostgreSql_AppliesMigrationsAndItsOwnHelperScript()
    {
        await using var container = new PostgreSqlBuilder().Build();
        await container.StartAsync(TestContext.Current.CancellationToken);

        var widget = await MigrateAndReadWidgetAsync(
            container.GetConnectionString(),
            static (opts, builder) => builder.UseNpgsql(opts.ConnectionString),
            DatabaseProvider.PostgreSql,
            // Unquoted identifiers fold to lower case here; EF created "Widgets".
            selectWidget: """SELECT "Name" FROM "Widgets" WHERE "Id" = 1""");

        Assert.Equal("from-postgresql-helper-script", widget);
    }

    [Fact(Explicit = true)]
    public async Task MySql_AppliesMigrationsAndItsOwnHelperScript()
    {
        await using var container = new MySqlBuilder().Build();
        await container.StartAsync(TestContext.Current.CancellationToken);

        var widget = await MigrateAndReadWidgetAsync(
            container.GetConnectionString(),
            static (opts, builder) => builder.UseMySql(
                opts.ConnectionString,
                ServerVersion.AutoDetect(opts.ConnectionString)),
            DatabaseProvider.MySql,
            selectWidget: "SELECT Name FROM Widgets WHERE Id = 1");

        Assert.Equal("from-mysql-helper-script", widget);
    }
}
