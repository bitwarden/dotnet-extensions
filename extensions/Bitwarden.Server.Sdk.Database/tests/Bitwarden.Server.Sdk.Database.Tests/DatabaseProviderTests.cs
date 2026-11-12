using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Database.Tests;

public class DatabaseProviderTests
{
    [Theory]
    [InlineData("Sqlite", DatabaseProvider.Sqlite)]
    [InlineData("SqlServer", DatabaseProvider.SqlServer)]
    [InlineData("PostgreSql", DatabaseProvider.PostgreSql)]
    [InlineData("MySql", DatabaseProvider.MySql)]
    [InlineData("MariaDb", DatabaseProvider.MariaDb)]
    [InlineData("mariadb", DatabaseProvider.MariaDb)]
    public void Provider_BindsFromConfiguration(string configured, DatabaseProvider expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OrderDatabase:Provider"] = configured,
                ["OrderDatabase:ConnectionString"] = "Server=localhost;Database=orders",
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<DatabaseOptions>("Order", configuration.GetSection("OrderDatabase"));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>().Get("Order");

        Assert.Equal(expected, options.Provider);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite, 0)]
    [InlineData(DatabaseProvider.SqlServer, 1)]
    [InlineData(DatabaseProvider.PostgreSql, 2)]
    [InlineData(DatabaseProvider.MySql, 3)]
    [InlineData(DatabaseProvider.MariaDb, 4)]
    public void Provider_KeepsItsNumericValue(DatabaseProvider provider, int value)
    {
        // Configuration may name an engine by number, and compiled consumers hold these as constants,
        // so a new engine is appended rather than inserted.
        Assert.Equal(value, (int)provider);
    }
}
