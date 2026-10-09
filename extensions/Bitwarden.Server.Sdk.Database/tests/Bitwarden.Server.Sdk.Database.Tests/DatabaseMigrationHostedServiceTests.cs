using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Bitwarden.Server.Sdk.Database.Tests;

public class DatabaseMigrationHostedServiceTests
{
    private static DatabaseMigrationHostedService BuildHostedService(
        IServiceCollection services,
        bool autoMigrate = true,
        TimeProvider? timeProvider = null)
    {
        services.ConfigureAll<AutoMigrateOptions>(o => o.AutoMigrate = autoMigrate);
        return BuildHostedServiceCore(services, timeProvider);
    }

    private static DatabaseMigrationHostedService BuildHostedServiceCore(
        IServiceCollection services,
        TimeProvider? timeProvider = null)
    {
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        return new DatabaseMigrationHostedService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            timeProvider ?? TimeProvider.System,
            serviceProvider.GetRequiredService<IOptionsMonitor<AutoMigrateOptions>>(),
            NullLogger<DatabaseMigrationHostedService>.Instance);
    }

    private static IDatabaseMigrator NamedMigrator(string name)
    {
        var migrator = Substitute.For<IDatabaseMigrator>();
        migrator.Name.Returns(name);
        return migrator;
    }

    [Fact]
    public async Task StartAsync_AutoMigrateDisabledForOneSchema_MigratesOnlyTheOther()
    {
        var vault = NamedMigrator("Vault");
        var billing = NamedMigrator("Billing");

        var services = new ServiceCollection();
        services.AddKeyedScoped<IDatabaseMigrator>("Vault", (_, _) => vault);
        services.AddKeyedScoped<IDatabaseMigrator>("Billing", (_, _) => billing);
        services.Configure<AutoMigrateOptions>("Vault", o => o.AutoMigrate = true);
        services.Configure<AutoMigrateOptions>("Billing", o => o.AutoMigrate = false);

        var hostedService = BuildHostedServiceCore(services);
        await hostedService.StartAsync(CancellationToken.None);

        await vault.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
        await billing.DidNotReceive().MigrateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_UnnamedConfigure_DoesNotDisableARegisteredSchema()
    {
        // Named options: configuring the default name says nothing about schema "Vault".
        var vault = NamedMigrator("Vault");

        var services = new ServiceCollection();
        services.AddKeyedScoped<IDatabaseMigrator>("Vault", (_, _) => vault);
        services.Configure<AutoMigrateOptions>("Vault", o => o.AutoMigrate = true);
        services.Configure<AutoMigrateOptions>(o => o.AutoMigrate = false);

        var hostedService = BuildHostedServiceCore(services);
        await hostedService.StartAsync(CancellationToken.None);

        await vault.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_NonInitialPhaseConfigured_SkipsThatSchema()
    {
        // Startup has no business applying a transition phase: those scripts are unjournaled and
        // would re-run on every boot. A real migrator, because the guard now asks the migrator for
        // its phase rather than reading SQL Server options for every schema.
        var services = new ServiceCollection();
        services.Configure<DatabaseOptions>("Vault", o =>
        {
            o.Provider = DatabaseProvider.SqlServer;
            o.ConnectionString = "Server=(local);Database=nope;Trusted_Connection=True";
        });
        services.Configure<SqlServerMigrationOptions>("Vault", o =>
        {
            o.Phase = MigrationPhase.Transition;
            o.ScriptPrefix = "Acme.Vault.SqlServer";
        });
        services.AddSqlServerDatabaseMigrator("Vault", typeof(DatabaseMigrationHostedServiceTests).Assembly);
        services.Configure<AutoMigrateOptions>("Vault", o => o.AutoMigrate = true);

        var hostedService = BuildHostedServiceCore(services);

        // Skipped, so nothing reaches the database — otherwise this connection attempt would fail.
        await hostedService.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_AnEfSchema_IsNotSubjectToThePhaseGuard()
    {
        // Phases are a SQL Server concept; a schema without them is never skipped by that check,
        // even if SqlServerMigrationOptions happen to be configured for its key.
        var vault = NamedMigrator("Vault");

        var services = new ServiceCollection();
        services.AddKeyedScoped<IDatabaseMigrator>("Vault", (_, _) => vault);
        services.Configure<SqlServerMigrationOptions>("Vault", o => o.Phase = MigrationPhase.Transition);

        var hostedService = BuildHostedService(services);
        await hostedService.StartAsync(CancellationToken.None);

        await vault.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_InvokesMigrateOnAllMigrators()
    {
        var migrator1 = Substitute.For<IDatabaseMigrator>();
        var migrator2 = Substitute.For<IDatabaseMigrator>();

        var services = new ServiceCollection();
        services.AddKeyedScoped<IDatabaseMigrator>("A", (_, _) => migrator1);
        services.AddKeyedScoped<IDatabaseMigrator>("B", (_, _) => migrator2);

        var hostedService = BuildHostedService(services);
        await hostedService.StartAsync(CancellationToken.None);

        await migrator1.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
        await migrator2.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_AutoMigrateDisabled_SkipsMigration()
    {
        var migrator = Substitute.For<IDatabaseMigrator>();

        var services = new ServiceCollection();
        services.AddKeyedScoped<IDatabaseMigrator>("Test", (_, _) => migrator);

        var hostedService = BuildHostedService(services, autoMigrate: false);
        await hostedService.StartAsync(CancellationToken.None);

        await migrator.DidNotReceive().MigrateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_PropagatesNonDbException()
    {
        var migrator = Substitute.For<IDatabaseMigrator>();
        migrator.MigrateAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("not a db error")));

        var services = new ServiceCollection();
        services.AddKeyedScoped<IDatabaseMigrator>("Test", (_, _) => migrator);

        var hostedService = BuildHostedService(services);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => hostedService.StartAsync(CancellationToken.None));

        // Non-DbException must not be retried — exactly one attempt
        await migrator.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_DbException_RetriesUntilSuccess()
    {
        var fakeTime = new FakeTimeProvider();
        var callCount = 0;
        var migrator = Substitute.For<IDatabaseMigrator>();
        migrator.MigrateAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (++callCount < 3)
                    return Task.FromException(new SqliteException("DB unavailable", 1));
                return Task.CompletedTask;
            });

        var services = new ServiceCollection();
        services.AddKeyedScoped<IDatabaseMigrator>("Test", (_, _) => migrator);

        var hostedService = BuildHostedService(services, timeProvider: fakeTime);
        var startTask = hostedService.StartAsync(CancellationToken.None);
        // State machine is suspended at Task.Delay after the first DbException
        fakeTime.Advance(TimeSpan.FromSeconds(20)); // resumes → second attempt fails → suspends
        fakeTime.Advance(TimeSpan.FromSeconds(20)); // resumes → third attempt succeeds
        await startTask;

        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task StartAsync_DbException_ExhaustsMaxAttempts_Throws()
    {
        var fakeTime = new FakeTimeProvider();
        var migrator = Substitute.For<IDatabaseMigrator>();
        migrator.MigrateAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new SqliteException("DB unavailable", 1)));

        var services = new ServiceCollection();
        services.AddKeyedScoped<IDatabaseMigrator>("Test", (_, _) => migrator);

        var hostedService = BuildHostedService(services, timeProvider: fakeTime);
        var startTask = hostedService.StartAsync(CancellationToken.None);
        // MaxAttempts = 10: first attempt plus 9 retries means 9 delays
        for (var i = 0; i < 9; i++)
            fakeTime.Advance(TimeSpan.FromSeconds(20));

        await Assert.ThrowsAsync<SqliteException>(() => startTask);
        await migrator.Received(10).MigrateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_Succeeds()
    {
        var hostedService = BuildHostedService(new ServiceCollection());
        await hostedService.StopAsync(CancellationToken.None);
    }
}
