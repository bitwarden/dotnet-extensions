using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bitwarden.Server.Sdk.Database.Analyzers.Tests;

/// <summary>
/// Why BW0026 reports a column relaxed to nullable: the previous release still maps it as required.
/// </summary>
public class PreviousReleaseReadTests
{
    [Theory]
    [ClassData(typeof(RowsTheNewReleaseLeftNull))]
    public async Task PreviousRelease_ReadingARowTheNewReleaseLeftNull_Throws(string description, string newReleaseInsert)
    {
        _ = description; // Names the case in test results.
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE Items (Id INTEGER PRIMARY KEY, Count INTEGER NULL, Name TEXT NULL); " + newReleaseInsert;
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var previousRelease = new PreviousReleaseContext(connection);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => previousRelease.Items.ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Rows the new release wrote after relaxing a column to nullable, each leaving a column the
    /// previous release maps as required holding <c>NULL</c>.
    /// </summary>
    public sealed class RowsTheNewReleaseLeftNull : TheoryData<string, string>
    {
        public RowsTheNewReleaseLeftNull()
        {
            Add("A required int left NULL", "INSERT INTO Items (Id, Count, Name) VALUES (1, NULL, 'a')");
            Add("A required string left NULL", "INSERT INTO Items (Id, Count, Name) VALUES (1, 2, NULL)");
        }
    }

    private sealed class Item
    {
        public int Id { get; set; }
        public int Count { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class PreviousReleaseContext(SqliteConnection connection) : DbContext
    {
        public DbSet<Item> Items => Set<Item>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);
    }
}
