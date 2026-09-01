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
}
