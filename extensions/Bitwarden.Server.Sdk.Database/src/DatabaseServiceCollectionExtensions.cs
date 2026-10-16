using System.ComponentModel;
using System.Reflection;
using Bitwarden.Server.Sdk.Environment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Database;

/// <summary>Extension methods for registering database migration services.</summary>
public static class DatabaseServiceCollectionExtensions
{
    /// <summary>
    /// Registers migration services for a database schema. Resolves connection configuration
    /// from named <see cref="DatabaseOptions"/> with the key <paramref name="name"/>.
    /// </summary>
    /// <typeparam name="TContext">The root DbContext for this schema.</typeparam>
    /// <typeparam name="TMigrationsAssembly">
    /// The migrations-assembly dispatcher that routes EF Core migration discovery to the
    /// correct provider-specific context subclass.
    /// </typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="name">
    /// Named-options key for <see cref="DatabaseOptions"/> and the keyed
    /// <see cref="IDatabaseMigrator"/> registration (e.g., <c>"Vault"</c>).
    /// </param>
    /// <param name="configureProvider">
    /// Configures the EF Core database provider on the <see cref="DbContextOptionsBuilder"/>.
    /// Supplied by the generated <c>Add{Schema}Database</c> extension method, which emits
    /// only the provider cases that are referenced in the consuming project.
    /// </param>
    public static IServiceCollection AddDatabase<TContext, TMigrationsAssembly>(
        this IServiceCollection services,
        string name,
        Action<DatabaseOptions, DbContextOptionsBuilder> configureProvider)
        where TContext : DbContext
        where TMigrationsAssembly : class, IMigrationsAssembly
    {
        services.AddDbContext<TContext>((sp, options) =>
        {
            var opts = sp.GetRequiredService<IOptionsMonitor<DatabaseOptions>>().Get(name);
            configureProvider(opts, options);
            options.ReplaceService<IMigrationsAssembly, TMigrationsAssembly>();
        });

        services.AddKeyedScoped<IDatabaseMigrator>(name, (sp, _) => CreateMigrator<TContext>(sp, name));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, DatabaseMigrationHostedService>());

        return services;
    }

    /// <summary>
    /// Registers SQL Server migration services using DbUp without registering a DbContext.
    /// Intended for SqlServer-migrator-only builds that do not need EF Core providers.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="name">
    /// Named-options key for <see cref="DatabaseOptions"/> and the keyed
    /// <see cref="IDatabaseMigrator"/> registration (e.g., <c>"Vault"</c>).
    /// </param>
    /// <param name="scriptsAssembly">Assembly that contains the embedded SQL scripts.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection AddSqlServerDatabaseMigrator(
        this IServiceCollection services,
        string name,
        Assembly scriptsAssembly)
    {
        services.AddKeyedScoped<IDatabaseMigrator>(name, (sp, _) => CreateSqlServerMigrator(sp, name, scriptsAssembly));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, DatabaseMigrationHostedService>());
        return services;
    }

    /// <summary>
    /// Applies pending migrations for a schema on startup, but only on self-hosted instances —
    /// Bitwarden-managed ones migrate as a separate deployment step.
    /// </summary>
    /// <remarks>
    /// Call this from the one service that owns the schema. Several services usually share a
    /// schema, and each of them migrating it on boot means concurrent migration runs against the
    /// same database.
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="name">
    /// Named-options key of the schema to migrate — the same key passed to
    /// <see cref="AddDatabase{TContext,TMigrationsAssembly}"/> (e.g. <c>"Vault"</c>).
    /// </param>
    public static IServiceCollection AutoMigrateWhenSelfHosted(this IServiceCollection services, string name)
    {
        services.AddBitwardenEnvironment();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, DatabaseMigrationHostedService>());

        services
            .AddOptions<AutoMigrateOptions>(name)
            .Configure<IBitwardenEnvironment>((options, environment) =>
                options.AutoMigrate = environment.SelfHosted);

        return services;
    }

    private static SqlServerMigrator CreateSqlServerMigrator(
        IServiceProvider serviceProvider, string name, Assembly scriptsAssembly) =>
        new(serviceProvider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>(),
            serviceProvider.GetRequiredService<IOptionsMonitor<SqlServerMigrationOptions>>(),
            name,
            scriptsAssembly,
            serviceProvider.GetRequiredService<ILogger<SqlServerMigrator>>(),
            serviceProvider.GetRequiredService<TimeProvider>());

    private static IDatabaseMigrator CreateMigrator<TContext>(IServiceProvider serviceProvider, string name)
        where TContext : DbContext
    {
        var opts = serviceProvider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>().Get(name);
        return opts.Provider switch
        {
            DatabaseProvider.SqlServer => new SqlServerMigrator(
                serviceProvider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>(),
                serviceProvider.GetRequiredService<IOptionsMonitor<SqlServerMigrationOptions>>(),
                name,
                typeof(TContext).Assembly,
                serviceProvider.GetRequiredService<ILogger<SqlServerMigrator>>(),
                serviceProvider.GetRequiredService<TimeProvider>()),

            _ => new EfCoreMigrator<TContext>(name, serviceProvider.GetRequiredService<TContext>()),
        };
    }

}
