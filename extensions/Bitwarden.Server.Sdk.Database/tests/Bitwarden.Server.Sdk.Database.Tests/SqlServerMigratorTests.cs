using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Bitwarden.Server.Sdk.Database.Tests;

public class SqlServerMigratorTests
{
    [Fact]
    public void Name_IsTheOptionsKeyTheMigratorWasBuiltWith()
    {
        var migrator = new SqlServerMigrator(
            Substitute.For<IOptionsMonitor<DatabaseOptions>>(),
            Substitute.For<IOptionsMonitor<SqlServerMigrationOptions>>(),
            "test",
            typeof(SqlServerMigratorTests).Assembly,
            NullLogger<SqlServerMigrator>.Instance,
            TimeProvider.System);

        Assert.Equal("test", migrator.Name);
    }

    [Fact]
    public void ResolveDatabase_WithoutADatabase_UsesTheLowerCasedRegistrationName()
    {
        var (connectionString, databaseName) = SqlServerMigrator.ResolveDatabase(
            "Server=localhost;User Id=sa;Password=pass", "Vault");

        Assert.Equal("vault", databaseName);
        Assert.Equal("vault", new SqlConnectionStringBuilder(connectionString).InitialCatalog);
    }

    [Fact]
    public void ResolveDatabase_WithADatabase_KeepsIt()
    {
        const string original = "Server=localhost;Database=vault_dev;User Id=sa;Password=pass";

        var (connectionString, databaseName) = SqlServerMigrator.ResolveDatabase(original, "Vault");

        Assert.Equal("vault_dev", databaseName);
        Assert.Equal("vault_dev", new SqlConnectionStringBuilder(connectionString).InitialCatalog);
    }
}
