using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Database;

/// <summary>
/// Helpers for database migration executables. Called from generated
/// <c>{SchemaName}MigrationApp</c>; not intended for direct use.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DatabaseMigrationCli
{
    /// <summary>
    /// Migrates a single database provider. Skips silently when
    /// <paramref name="connectionString"/> is null or whitespace.
    /// </summary>
    /// <param name="dbProvider">The provider to migrate.</param>
    /// <param name="connectionString">Connection string; <see langword="null"/> skips the provider.</param>
    /// <param name="migratorKey">Named-options key (e.g. <c>"Vault"</c>).</param>
    /// <param name="registerDatabase">
    /// Registers schema-specific services on the service collection
    /// (e.g. <c>services.AddVaultDatabase()</c>).
    /// </param>

    public static async Task MigrateOneAsync(
        DatabaseProvider dbProvider,
        string? connectionString,
        string migratorKey,
        Action<IServiceCollection> registerDatabase)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine($"Warning: {dbProvider} connection string not provided, skipping.");
            return;
        }

        Console.WriteLine($"Starting {dbProvider} migrations");

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
        services.Configure<DatabaseOptions>(migratorKey, opts =>
        {
            opts.Provider = dbProvider;
            opts.ConnectionString = connectionString;
        });
        registerDatabase(services);

        await using var serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredKeyedService<IDatabaseMigrator>(migratorKey)
            .MigrateAsync();
    }

    /// <summary>
    /// Runs the SQL Server migrator, or prints what it would apply when
    /// <paramref name="dryRun"/> is set. Skips silently when <paramref name="connectionString"/> is null or
    /// whitespace.
    /// </summary>
    /// <param name="connectionString">Connection string; <see langword="null"/> skips the run.</param>
    /// <param name="migratorKey">Named-options key (e.g. <c>"Vault"</c>).</param>
    /// <param name="registerDatabase">Registers schema-specific services on the service collection.</param>
    /// <param name="phase">Which script set to apply.</param>
    /// <param name="noTransaction">Runs scripts without a wrapping transaction.</param>
    /// <param name="dryRun">Prints the pending scripts instead of applying them.</param>
    public static async Task RunSqlServerAsync(
        string? connectionString,
        string migratorKey,
        Action<IServiceCollection> registerDatabase,
        MigrationPhase phase = MigrationPhase.Initial,
        bool noTransaction = false,
        bool dryRun = false)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("Warning: SqlServer connection string not provided, skipping.");
            return;
        }

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
        services.Configure<DatabaseOptions>(migratorKey, opts =>
        {
            opts.Provider = DatabaseProvider.SqlServer;
            opts.ConnectionString = connectionString;
        });
        services.Configure<SqlServerMigrationOptions>(migratorKey, opts =>
        {
            opts.Phase = phase;
            opts.NoTransaction = noTransaction;
            opts.DryRun = dryRun;
        });
        registerDatabase(services);

        await using var serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();
        Console.WriteLine(dryRun
            ? $"Listing pending SqlServer scripts ({phase} phase)"
            : $"Starting SqlServer migrations ({phase} phase)");

        await scope.ServiceProvider
            .GetRequiredKeyedService<IDatabaseMigrator>(migratorKey)
            .MigrateAsync();
    }

    /// <summary>
    /// Invokes <c>dotnet ef migrations add</c> for each EF provider. Called from generated
    /// code guarded by <c>#if DEBUG</c>.
    /// </summary>
    /// <param name="migrationName">Name of the migration to scaffold.</param>
    /// <param name="projectDir">Absolute path to the migration project directory.</param>
    /// <param name="efProviders">EF provider descriptors (context name, output dir, namespace).</param>
    public static async Task AddMigrationsAsync(
        string migrationName,
        string projectDir,
        (string Context, string OutputDir, string Namespace)[] efProviders)
    {
        foreach (var (context, outputDir, ns) in efProviders)
        {
            Console.WriteLine($"--- Adding migration '{migrationName}' for {context} ---");

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList =
                {
                    "ef", "migrations", "add", migrationName,
                    "--context", context,
                    "--output-dir", outputDir,
                    "--namespace", ns,
                    "--project", projectDir,
                },
                WorkingDirectory = projectDir,
            }) ?? throw new InvalidOperationException("Failed to start dotnet ef process.");

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                Console.Error.WriteLine(
                    $"Warning: migration generation failed for {context} (exit code {process.ExitCode}). Skipping.");
        }
    }
}
