using Bitwarden.Server.Sdk.Environment;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Bitwarden.Server.Sdk.Database.Tests;

public class DatabaseServiceCollectionExtensionsTests
{
    private static DatabaseFixture BuildFixture(Action<IServiceCollection>? configure = null)
        => SqliteTestDatabase.Create<TestDbContext, EmptyMigrationsAssembly>(configure: configure);

    [Fact]
    public async Task AddDatabase_ResolvesDbContext()
    {
        await using var fixture = BuildFixture();
        using var scope = fixture.ServiceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        Assert.NotNull(context);
    }

    [Fact]
    public async Task AddDatabase_ConfiguresSqliteProvider()
    {
        await using var fixture = BuildFixture();
        using var scope = fixture.ServiceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var extensions = context.Database.GetService<IDbContextOptions>().Extensions;
        Assert.Contains(extensions, e => e.GetType().Name.Contains("Sqlite"));
    }

    [Fact]
    public async Task AddDatabase_RegistersKeyedMigrator()
    {
        await using var fixture = BuildFixture();
        using var scope = fixture.ServiceProvider.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredKeyedService<IDatabaseMigrator>("Test");
        Assert.NotNull(migrator);
    }

    [Fact]
    public void AddDatabase_RegistersHostedService()
    {
        var services = new ServiceCollection();
        services.AddSqliteTestDatabase<TestDbContext>("Test");

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(DatabaseMigrationHostedService));
    }

    [Fact]
    public void AddDatabase_CalledTwice_HostedServiceRegisteredOnce()
    {
        var services = new ServiceCollection();
        // Calling AddDatabase twice with the same name shouldn't register the hosted service twice
        services.AddSqliteTestDatabase<TestDbContext>("Test");
        services.AddSqliteTestDatabase<TestDbContext>("Test");

        var count = services.Count(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(DatabaseMigrationHostedService));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task AddDatabase_MigratorIsNamedAfterItsKey()
    {
        await using var fixture = BuildFixture();
        using var scope = fixture.ServiceProvider.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredKeyedService<IDatabaseMigrator>("Test");

        // The name is what lets a caller holding a migrator resolve the options that configured it.
        Assert.Equal("Test", migrator.Name);
    }

    [Fact]
    public async Task AddDatabase_DoesNotMigrateOnStartupByItself()
    {
        // Registering a schema says nothing about who applies it; several services share one.
        await using var fixture = BuildFixture();
        var options = fixture.ServiceProvider.GetRequiredService<IOptionsMonitor<AutoMigrateOptions>>();

        Assert.False(options.Get("Test").AutoMigrate);
    }

    [Fact]
    public async Task AutoMigrateWhenSelfHosted_OnASelfHostedInstance_TurnsItOn()
    {
        var env = Substitute.For<IBitwardenEnvironment>();
        env.SelfHosted.Returns(true);

        await using var fixture = BuildFixture(services =>
        {
            services.AddSingleton(env);
            services.AutoMigrateWhenSelfHosted("Test");
        });

        // The hosted service reads the schema's name, so that is the name that has to be configured.
        var options = fixture.ServiceProvider.GetRequiredService<IOptionsMonitor<AutoMigrateOptions>>();
        Assert.True(options.Get("Test").AutoMigrate);
    }

    [Fact]
    public async Task AutoMigrateWhenSelfHosted_OnACloudInstance_LeavesItOff()
    {
        // Bitwarden-managed instances migrate as a separate deployment step.
        var env = Substitute.For<IBitwardenEnvironment>();
        env.SelfHosted.Returns(false);

        await using var fixture = BuildFixture(services =>
        {
            services.AddSingleton(env);
            services.AutoMigrateWhenSelfHosted("Test");
        });

        var options = fixture.ServiceProvider.GetRequiredService<IOptionsMonitor<AutoMigrateOptions>>();
        Assert.False(options.Get("Test").AutoMigrate);
    }

    [Fact]
    public async Task AutoMigrateWhenSelfHosted_OnlyAffectsTheSchemaItNames()
    {
        var env = Substitute.For<IBitwardenEnvironment>();
        env.SelfHosted.Returns(true);

        await using var fixture = BuildFixture(services =>
        {
            services.AddSingleton(env);
            services.AutoMigrateWhenSelfHosted("Test");
        });

        var options = fixture.ServiceProvider.GetRequiredService<IOptionsMonitor<AutoMigrateOptions>>();
        Assert.False(options.Get("SomeoneElsesSchema").AutoMigrate);
    }
}

public class AddSqlServerDatabaseMigratorTests
{
    private static IServiceCollection BuildServices(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<DatabaseOptions>("Test", opts =>
        {
            opts.Provider = DatabaseProvider.SqlServer;
            opts.ConnectionString = "Server=localhost;Database=test;";
        });
        services.AddSqlServerDatabaseMigrator("Test", typeof(AddSqlServerDatabaseMigratorTests).Assembly);
        configure?.Invoke(services);
        return services;
    }

    [Fact]
    public void AddSqlServerDatabaseMigrator_RegistersKeyedMigrator()
    {
        var services = BuildServices();
        Assert.Contains(services, d =>
            d.ServiceType == typeof(IDatabaseMigrator) &&
            d.ServiceKey is "Test" &&
            d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddSqlServerDatabaseMigrator_RegistersHostedService()
    {
        var services = BuildServices();
        Assert.Contains(services, d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(DatabaseMigrationHostedService));
    }

    [Fact]
    public void AddSqlServerDatabaseMigrator_CalledTwice_HostedServiceRegisteredOnce()
    {
        var services = new ServiceCollection();
        services.AddSqlServerDatabaseMigrator("Test", typeof(AddSqlServerDatabaseMigratorTests).Assembly);
        services.AddSqlServerDatabaseMigrator("Test", typeof(AddSqlServerDatabaseMigratorTests).Assembly);

        var count = services.Count(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(DatabaseMigrationHostedService));
        Assert.Equal(1, count);
    }

    [Fact]
    public void AddSqlServerDatabaseMigrator_WithAutoMigrateWhenSelfHosted_TurnsItOnForThatSchema()
    {
        var env = Substitute.For<IBitwardenEnvironment>();
        env.SelfHosted.Returns(true);

        var services = BuildServices(s =>
        {
            s.AddSingleton(env);
            s.AutoMigrateWhenSelfHosted("Test");
        });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptionsMonitor<AutoMigrateOptions>>();

        Assert.True(options.Get("Test").AutoMigrate);
    }

    [Fact]
    public void AutoMigrateWhenSelfHosted_AndADirectConfigure_AreOrderSensitive()
    {
        // Documented in PACKAGE.md: both turn migration on, configuration actions run in
        // registration order, and the last one wins — so the two are not interchangeable.
        var env = Substitute.For<IBitwardenEnvironment>();
        env.SelfHosted.Returns(false);

        var optInFirst = BuildServices(s =>
        {
            s.AddSingleton(env);
            s.AutoMigrateWhenSelfHosted("Test");
            s.Configure<AutoMigrateOptions>("Test", o => o.AutoMigrate = true);
        });

        var optInLast = BuildServices(s =>
        {
            s.AddSingleton(env);
            s.Configure<AutoMigrateOptions>("Test", o => o.AutoMigrate = true);
            s.AutoMigrateWhenSelfHosted("Test");
        });

        using var withConfigureLast = optInFirst.BuildServiceProvider();
        using var withOptInLast = optInLast.BuildServiceProvider();

        Assert.True(withConfigureLast.GetRequiredService<IOptionsMonitor<AutoMigrateOptions>>()
            .Get("Test").AutoMigrate);

        // The opt-in overwrites the unconditional true with whether the instance is self-hosted.
        Assert.False(withOptInLast.GetRequiredService<IOptionsMonitor<AutoMigrateOptions>>()
            .Get("Test").AutoMigrate);
    }

    [Fact]
    public void AddSqlServerDatabaseMigrator_DoesNotMigrateOnStartupByItself()
    {
        using var serviceProvider = BuildServices().BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptionsMonitor<AutoMigrateOptions>>();

        Assert.False(options.Get("Test").AutoMigrate);
    }
}

public class MultipleSchemaTests
{
    [Fact]
    public async Task AddDatabase_TwoSchemas_EachMigratorRetrievableByKey_HostedServiceRegisteredOnce()
    {
        var vaultConn = $"Data Source={Guid.NewGuid()};Mode=Memory;Cache=Shared";
        var smConn = $"Data Source={Guid.NewGuid()};Mode=Memory;Cache=Shared";

        await using var vaultKeepAlive = new SqliteConnection(vaultConn);
        await using var smKeepAlive = new SqliteConnection(smConn);
        vaultKeepAlive.Open();
        smKeepAlive.Open();

        // Self-hosted, so the opt-in below turns migration on; overrides RuntimeBitwardenEnvironment,
        // which needs IHostEnvironment
        var env = Substitute.For<IBitwardenEnvironment>();
        env.SelfHosted.Returns(true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<DatabaseOptions>("Vault", opts =>
        {
            opts.Provider = DatabaseProvider.Sqlite;
            opts.ConnectionString = vaultConn;
        });
        services.Configure<DatabaseOptions>("SecretsManager", opts =>
        {
            opts.Provider = DatabaseProvider.Sqlite;
            opts.ConnectionString = smConn;
        });
        services.AddSqliteTestDatabase<TestDbContext>("Vault");
        services.AddSqliteTestDatabase<AnotherTestDbContext>("SecretsManager");
        services.AddSingleton(env);

        // This host owns both schemas, so it asks for both.
        services.AutoMigrateWhenSelfHosted("Vault");
        services.AutoMigrateWhenSelfHosted("SecretsManager");

        // One hosted service registration despite two AddDatabase calls
        Assert.Equal(1, services.Count(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(DatabaseMigrationHostedService)));

        await using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        // Each schema's migrator is retrievable by its own key, as a distinct instance
        var vaultMigrator = scope.ServiceProvider.GetRequiredKeyedService<IDatabaseMigrator>("Vault");
        var smMigrator = scope.ServiceProvider.GetRequiredKeyedService<IDatabaseMigrator>("SecretsManager");
        Assert.NotSame(vaultMigrator, smMigrator);
        Assert.Equal("Vault", vaultMigrator.Name);
        Assert.Equal("SecretsManager", smMigrator.Name);

        // Single hosted service instance runs both schema migrations; EF Core creates
        // __EFMigrationsHistory on each database as a concrete side effect of MigrateAsync running
        var hostedServices = serviceProvider.GetServices<IHostedService>().ToList();
        Assert.Single(hostedServices);
        await hostedServices[0].StartAsync(TestContext.Current.CancellationToken);

        await AssertMigratedAsync(vaultKeepAlive);
        await AssertMigratedAsync(smKeepAlive);
    }

    private static async Task AssertMigratedAsync(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'";
        Assert.Equal(1L, await cmd.ExecuteScalarAsync());
    }
}
