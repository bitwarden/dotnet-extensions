using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Bitwarden.Server.Sdk.Database.Tests;

/// <summary>
/// <see cref="DatabaseMigrationHostedService"/> started through a host, migrating a real SQL Server.
/// </summary>
public sealed class DatabaseMigrationHostedServiceIntegrationTests : IClassFixture<SqlServerFixture>
{
    private const string ScriptPrefix = "Bitwarden.Server.Sdk.Database.Tests.Migrations.SqlServer";

    /// <summary>
    /// A value of the kind SQL Server puts in its error messages, e.g. a duplicate key.
    /// </summary>
    private const string RowValue = "alice@example.com";

    private readonly SqlServerFixture _sqlServer;

    public DatabaseMigrationHostedServiceIntegrationTests(SqlServerFixture sqlServer)
    {
        _sqlServer = sqlServer;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        using var cmd = new SqlCommand(sql, connection);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A database whose first migration script fails the same way on every run, as a script with a
    /// bug would, with a real duplicate key error that carries <see cref="RowValue"/>. The upgrade's
    /// transaction rolls back anything else the trigger could record, so a sequence counts the runs.
    /// </summary>
    private async Task<string> StageAlwaysFailingScriptAsync(string databaseName)
    {
        var connectionString = await _sqlServer.ConnectionStringForAsync(databaseName);

        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString;
        await ExecuteAsync(master, $"CREATE DATABASE [{databaseName}];");

        await ExecuteAsync(connectionString, "CREATE SEQUENCE [dbo].[ScriptRuns] START WITH 1;");
        await ExecuteAsync(connectionString, $"""
            CREATE TABLE [dbo].[Contacts] ([Email] NVARCHAR(200) NOT NULL PRIMARY KEY);
            INSERT INTO [dbo].[Contacts] ([Email]) VALUES (N'{RowValue}');
            """);
        await ExecuteAsync(connectionString, $"""
            CREATE TRIGGER trg_always_fail ON DATABASE FOR CREATE_TABLE AS
            BEGIN
                IF EVENTDATA().value('(/EVENT_INSTANCE/ObjectName)[1]', 'nvarchar(128)') = N'Orders'
                BEGIN
                    DECLARE @run BIGINT = NEXT VALUE FOR [dbo].[ScriptRuns];
                    INSERT INTO [dbo].[Contacts] ([Email]) VALUES (N'{RowValue}');
                END
            END;
            """);

        return connectionString;
    }

    /// <summary>
    /// Starts a host that auto-migrates the schema, and returns how its start ended.
    /// </summary>
    private static async Task<(Exception? Error, FakeLogCollector Logs)> StartHostAsync(string connectionString)
    {
        var time = new FakeTimeProvider();
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(builder => builder.AddFakeLogging());
                services.Configure<DatabaseOptions>("Test", opts =>
                {
                    opts.Provider = DatabaseProvider.SqlServer;
                    opts.ConnectionString = connectionString;
                });
                services.Configure<SqlServerMigrationOptions>("Test", opts => opts.ScriptPrefix = ScriptPrefix);
                services.Configure<AutoMigrateOptions>("Test", opts => opts.AutoMigrate = true);
                services.AddSqlServerDatabaseMigrator("Test", typeof(DatabaseMigrationHostedServiceIntegrationTests).Assembly);

                // Registered last so it wins: the waits between attempts are not worth serving.
                services.AddSingleton<TimeProvider>(time);
            })
            .Build();

        var logs = host.Services.GetRequiredService<FakeLogCollector>();
        var starting = host.StartAsync(TestContext.Current.CancellationToken);
        for (var i = 0; i < 400 && !starting.IsCompleted; i++)
        {
            time.Advance(TimeSpan.FromSeconds(20));
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        try
        {
            await starting;
            return (null, logs);
        }
        catch (Exception e)
        {
            return (e, logs);
        }
    }

    [Fact(Explicit = true)]
    public async Task StartAsync_AScriptThatAlwaysFails_IsAttemptedOnce()
    {
        var connectionString = await StageAlwaysFailingScriptAsync("hosted_script_failure");

        var (error, _) = await StartHostAsync(connectionString);

        Assert.NotNull(error);

        // Retrying cannot fix a failing script: the run is one transaction, so every attempt fails
        // the same way and only delays the error.
        var runs = await ScalarAsync(
            connectionString, "SELECT CAST([current_value] AS BIGINT) FROM sys.sequences WHERE [name] = 'ScriptRuns'");
        Assert.Equal(1, runs);
    }

    [Fact(Explicit = true)]
    public async Task StartAsync_AScriptThatFails_KeepsTheDatabasesMessageOutOfTheLogs()
    {
        var connectionString = await StageAlwaysFailingScriptAsync("hosted_script_failure_logs");

        var (error, logs) = await StartHostAsync(connectionString);

        // What stops the host still identifies the failure, by the database's error number.
        var failure = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("SQL Server error 2627", failure.Message);
        Assert.DoesNotContain(RowValue, failure.ToString());

        // No log repeats the database's message: not this package's, DbUp's, or the host's own
        // record of the exception that stopped it.
        Assert.DoesNotContain(
            logs.GetSnapshot(),
            record => record.Message.Contains(RowValue) || (record.Exception?.ToString().Contains(RowValue) ?? false));
    }
}
