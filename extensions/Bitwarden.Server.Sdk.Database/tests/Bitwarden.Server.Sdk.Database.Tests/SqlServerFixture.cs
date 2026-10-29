using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Bitwarden.Server.Sdk.Database.Tests;

/// <summary>
/// A real SQL Server, shared by the tests in a class. DbUp, the journal, and the phase filters all
/// depend on server behaviour that nothing in-process can stand in for.
/// </summary>
/// <remarks>
/// Nothing touches Docker until a test asks for a connection string. xUnit builds a class fixture
/// even when every test in the class is skipped, and building a container resolves Docker
/// configuration — so doing either eagerly would break the fast suite on a machine without Docker,
/// which is exactly the machine that skipped these tests.
/// </remarks>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MsSqlContainer? _container;

    /// <summary>Connection string for a database of the given name, created if it doesn't exist.</summary>
    public async Task<string> ConnectionStringForAsync(string databaseName)
    {
        var container = await EnsureStartedAsync();

        return new SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = databaseName,
            TrustServerCertificate = true,
        }.ConnectionString;
    }

    private async Task<MsSqlContainer> EnsureStartedAsync()
    {
        if (_container is not null)
            return _container;

        await _gate.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (_container is null)
            {
                var container = new MsSqlBuilder().Build();
                await container.StartAsync(TestContext.Current.CancellationToken);

                // Assigned only once started, so a failed start is retried rather than cached.
                _container = container;
            }
        }
        finally
        {
            _gate.Release();
        }

        return _container;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _gate.Dispose();

        if (_container is not null)
            await _container.DisposeAsync();
    }
}
