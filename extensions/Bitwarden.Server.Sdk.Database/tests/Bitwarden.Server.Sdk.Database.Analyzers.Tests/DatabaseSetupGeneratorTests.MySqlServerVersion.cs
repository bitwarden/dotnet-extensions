using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Bitwarden.Server.Sdk.Database.Analyzers.Tests;

/// <summary>
/// The generated MySQL provider configuration asks each server its version once per process, not
/// each time a context's options are built.
/// </summary>
public partial class DatabaseSetupGeneratorTests
{
    [Fact]
    public async Task MySqlServerVersion_IsDetectedOncePerServer()
    {
        var assembly = EmitAndLoad(RunGenerator(await BuildCompilationAsync(SchemaWithMigration)));
        await using var server = FakeMySqlServer.Start();
        await using var other = FakeMySqlServer.Start();

        for (var i = 0; i < 5; i++)
            BuildContextOptions(assembly, server.ConnectionString("orders"));

        // The version belongs to the server, so another database or account on it doesn't ask again.
        BuildContextOptions(assembly, server.ConnectionString("vault", user: "someone-else"));
        Assert.Equal(1, server.Connections);

        // Another server is asked its own.
        BuildContextOptions(assembly, other.ConnectionString("orders"));
        BuildContextOptions(assembly, other.ConnectionString("orders"));
        Assert.Equal(1, other.Connections);
    }

    [Fact]
    public async Task MySqlServerVersion_FailedDetectionIsNotCached()
    {
        var assembly = EmitAndLoad(RunGenerator(await BuildCompilationAsync(SchemaWithMigration)));
        await using var server = FakeMySqlServer.Start();
        var connectionString = server.ConnectionString("orders");

        server.RefuseConnections = true;
        Assert.Throws<MySqlException>(() => BuildContextOptions(assembly, connectionString));
        Assert.Throws<MySqlException>(() => BuildContextOptions(assembly, connectionString));
        Assert.Equal(2, server.Connections);

        // Once the server answers, the next build asks it, and the builds after that don't.
        server.RefuseConnections = false;
        BuildContextOptions(assembly, connectionString);
        BuildContextOptions(assembly, connectionString);
        Assert.Equal(3, server.Connections);
    }

    /// <summary>
    /// Builds the context's options through the generated registration, as each scope that asks for
    /// the context does. A fresh service provider each time, so nothing is reused but what the
    /// generated code keeps itself.
    /// </summary>
    private static void BuildContextOptions(Assembly generated, string connectionString)
    {
        using var provider = BuildProviderFor(generated, DatabaseProvider.MySql, connectionString);
        using var scope = provider.CreateScope();

        var context = (DbContext)scope.ServiceProvider.GetRequiredService(generated.GetType("My.App.MyDatabaseContext")!);

        Assert.Equal("Pomelo.EntityFrameworkCore.MySql", context.Database.ProviderName);
    }
}
