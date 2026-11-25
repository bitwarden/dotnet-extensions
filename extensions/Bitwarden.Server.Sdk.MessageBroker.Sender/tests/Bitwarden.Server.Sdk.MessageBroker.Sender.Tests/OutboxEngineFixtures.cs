using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.MariaDb;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

public abstract class OutboxEngineFixture : IAsyncLifetime
{
    /// <summary>
    /// A context on its own connection, outside any instance's container.
    /// </summary>
    public ShopContext Database()
    {
        var options = new DbContextOptionsBuilder<ShopContext>();
        Configure(options);
        return new ShopContext(options.Options);
    }

    public void Configure(DbContextOptionsBuilder options)
    {
        ConfigureEngine(options);
        // This process opens every engine: past twenty option sets is by design here.
        options.ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning));
    }

    public async ValueTask InitializeAsync()
    {
        await StartAsync();
        await using var database = Database();
        await database.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public abstract ValueTask DisposeAsync();

    protected abstract Task StartAsync();

    protected abstract void ConfigureEngine(DbContextOptionsBuilder options);
}

/// <summary>
/// SQL Server, configured to retry transient failures, as a cloud deployment is.
/// </summary>
public sealed class SqlServerOutboxFixture : OutboxEngineFixture
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public override async ValueTask DisposeAsync() => await _container.DisposeAsync();

    protected override Task StartAsync() => _container.StartAsync(TestContext.Current.CancellationToken);

    protected override void ConfigureEngine(DbContextOptionsBuilder options) =>
        options.UseSqlServer(_container.GetConnectionString(), sql => sql.EnableRetryOnFailure());
}

public sealed class PostgreSqlOutboxFixture : OutboxEngineFixture
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:16").Build();

    public override async ValueTask DisposeAsync() => await _container.DisposeAsync();

    protected override Task StartAsync() => _container.StartAsync(TestContext.Current.CancellationToken);

    protected override void ConfigureEngine(DbContextOptionsBuilder options) =>
        options.UseNpgsql(_container.GetConnectionString());
}

/// <remarks>
/// Microting's fork of Pomelo, the MySQL provider Bitwarden's server uses, whose own releases stop at EF Core 9.
/// </remarks>
public sealed class MySqlOutboxFixture : OutboxEngineFixture
{
    private readonly MySqlContainer _container = new MySqlBuilder().WithImage("mysql:8.4").Build();
    private ServerVersion? _version;

    public override async ValueTask DisposeAsync() => await _container.DisposeAsync();

    protected override Task StartAsync() => _container.StartAsync(TestContext.Current.CancellationToken);

    protected override void ConfigureEngine(DbContextOptionsBuilder options) =>
        options.UseMySql(_container.GetConnectionString(), _version ??= ServerVersion.AutoDetect(_container.GetConnectionString()));
}

public sealed class MariaDbOutboxFixture : OutboxEngineFixture
{
    private readonly MariaDbContainer _container = new MariaDbBuilder().WithImage("mariadb:11.4").Build();
    private ServerVersion? _version;

    public override async ValueTask DisposeAsync() => await _container.DisposeAsync();

    protected override Task StartAsync() => _container.StartAsync(TestContext.Current.CancellationToken);

    protected override void ConfigureEngine(DbContextOptionsBuilder options) =>
        options.UseMySql(_container.GetConnectionString(), _version ??= ServerVersion.AutoDetect(_container.GetConnectionString()));
}
