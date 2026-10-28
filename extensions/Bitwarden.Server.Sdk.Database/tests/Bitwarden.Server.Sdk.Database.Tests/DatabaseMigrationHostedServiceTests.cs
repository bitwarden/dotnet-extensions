using System.Data.Common;
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

    /// <summary>A migrator whose server refuses connections for a while, and whose migration may fail.</summary>
    private sealed class FlakyServerMigrator : IDatabaseMigrator, IDatabaseAvailability
    {
        private readonly int _refusedChecks;
        private readonly DbException? _migrationError;

        public FlakyServerMigrator(int refusedChecks, DbException? migrationError = null)
        {
            _refusedChecks = refusedChecks;
            _migrationError = migrationError;
        }

        public string Name => "Test";
        public int Checks { get; private set; }
        public int Migrations { get; private set; }

        public Task<DbException?> CheckConnectionAsync(CancellationToken cancellationToken)
            => Task.FromResult<DbException?>(
                ++Checks <= _refusedChecks ? new SqliteException("unable to open database file", 14) : null);

        public Task MigrateAsync(CancellationToken cancellationToken = default)
        {
            Migrations++;
            return _migrationError is null ? Task.CompletedTask : Task.FromException(_migrationError);
        }
    }

    /// <summary>Advances the clock until startup settles; the waits between checks vary.</summary>
    private static async Task SettleAsync(FakeTimeProvider time, Task starting)
    {
        for (var i = 0; i < 100 && !starting.IsCompleted; i++)
        {
            time.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task StartAsync_DatabaseNotAcceptingConnectionsYet_WaitsThenMigratesOnce()
    {
        var time = new FakeTimeProvider();
        var migrator = new FlakyServerMigrator(refusedChecks: 2);

        var services = new ServiceCollection();
        services.AddKeyedScoped<IDatabaseMigrator>("Test", (_, _) => migrator);

        var hostedService = BuildHostedService(services, timeProvider: time);
        var starting = hostedService.StartAsync(CancellationToken.None);
        await SettleAsync(time, starting);
        await starting;

        Assert.Equal(3, migrator.Checks);
        Assert.Equal(1, migrator.Migrations);
    }

    [Fact]
    public async Task StartAsync_DatabaseNeverAcceptsConnections_GivesUpWithoutMigrating()
    {
        var time = new FakeTimeProvider();
        var migrator = new FlakyServerMigrator(refusedChecks: int.MaxValue);

        var services = new ServiceCollection();
        services.AddKeyedScoped<IDatabaseMigrator>("Test", (_, _) => migrator);

        var hostedService = BuildHostedService(services, timeProvider: time);
        var starting = hostedService.StartAsync(CancellationToken.None);
        await SettleAsync(time, starting);

        await Assert.ThrowsAsync<SqliteException>(() => starting);
        Assert.Equal(0, migrator.Migrations);
    }

    [Fact]
    public async Task StartAsync_MigrationFailsWithADbException_IsNotRetried()
    {
        var migrator = new FlakyServerMigrator(
            refusedChecks: 0, migrationError: new SqliteException("UNIQUE constraint failed: Orders.Name", 19));

        var services = new ServiceCollection();
        services.AddKeyedScoped<IDatabaseMigrator>("Test", (_, _) => migrator);

        var hostedService = BuildHostedService(services, timeProvider: new FakeTimeProvider());

        await Assert.ThrowsAsync<SqliteException>(() => hostedService.StartAsync(CancellationToken.None));
        Assert.Equal(1, migrator.Migrations);
    }

    [Fact]
    public async Task StopAsync_Succeeds()
    {
        var hostedService = BuildHostedService(new ServiceCollection());
        await hostedService.StopAsync(CancellationToken.None);
    }
}
