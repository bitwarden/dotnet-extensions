using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bitwarden.Server.Sdk.Database.Analyzers.Tests;

/// <summary>
/// Why the rename guide's batched copy needs <c>suppressTransaction</c>: EF Core commits a
/// migration's steps together, so batches inside it release their locks only at the end.
/// </summary>
public class MigrationTransactionTests
{
    [Fact]
    public async Task BatchedCopyInAMigration_CommitsOnlyAfterTheLastBatch()
    {
        var events = await MigrateAsync<CopyInOneTransactionContext>();

        Assert.Equal(["batch 1 in a transaction", "batch 2 in a transaction", "commit"], events.SkipWhile(e => e == "commit").Take(3));
    }

    [Fact]
    public async Task BatchedCopyWithSuppressTransaction_RunsEachBatchOutsideATransaction()
    {
        var events = await MigrateAsync<CopyOutsideTransactionContext>();

        Assert.Equal(["batch 1 on its own", "batch 2 on its own"], events.Where(e => e != "commit"));
    }

    private static async Task<List<string>> MigrateAsync<TContext>()
        where TContext : DbContext
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        List<string> events = [];
        var options = new DbContextOptionsBuilder<TContext>()
            .UseSqlite(connection)
            .AddInterceptors(new BatchRecorder(events), new CommitRecorder(events))
            .Options;
        await using var context = (TContext)Activator.CreateInstance(typeof(TContext), options)!;

        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        return events;
    }

    private sealed class BatchRecorder(List<string> events) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var batch = command.CommandText.IndexOf("batch", StringComparison.Ordinal);
            if (batch >= 0)
            {
                events.Add(command.CommandText.Substring(batch, 7) + (command.Transaction is null ? " on its own" : " in a transaction"));
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class CommitRecorder(List<string> events) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            events.Add("commit");
            return Task.CompletedTask;
        }
    }

    public sealed class CopyInOneTransactionContext(DbContextOptions<CopyInOneTransactionContext> options) : DbContext(options);

    public sealed class CopyOutsideTransactionContext(DbContextOptions<CopyOutsideTransactionContext> options) : DbContext(options);

    [DbContext(typeof(CopyInOneTransactionContext))]
    [Migration("20260101000000_CopyInBatches")]
    public sealed class CopyInBatches : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SELECT 1 -- batch 1");
            migrationBuilder.Sql("SELECT 1 -- batch 2");
        }
    }

    [DbContext(typeof(CopyOutsideTransactionContext))]
    [Migration("20260101000000_CopyInBatchesOutsideTransaction")]
    public sealed class CopyInBatchesOutsideTransaction : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SELECT 1 -- batch 1", suppressTransaction: true);
            migrationBuilder.Sql("SELECT 1 -- batch 2", suppressTransaction: true);
        }
    }
}
