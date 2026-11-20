using System.Data.Common;
using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Resources.Caching.Tests;

/// <summary>
/// A change tells the cache just before it commits: every save, a single statement included, commits in a transaction
/// whose commit invalidates first, and the tombstone marks the version the change replaced dead.
/// </summary>
public sealed class CacheTransactionInterceptorTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private static readonly Guid Ours = Guid.NewGuid();

    private readonly Guid _book = Guid.NewGuid();
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"shelf-{Guid.NewGuid():N}.db");
    private ConnectionMultiplexer _redis = null!;

    private IDatabase Redis => _redis.GetDatabase();

    private string BookKey => $"{ShelfHost.ServiceName}:book:{_book}";

    public async ValueTask InitializeAsync() => _redis = await ConnectionMultiplexer.ConnectAsync(redis.Hostname);

    public async ValueTask DisposeAsync()
    {
        await _redis.DisposeAsync();
        SqliteConnection.ClearAllPools();

        foreach (var file in new[] { _file, _file + "-wal", _file + "-shm" })
        {
            File.Delete(file);
        }
    }

    // A save of one row is one statement, which Entity Framework would send without a transaction. The cache makes it
    // open one, and its commit sees the tombstone written while another connection still reads the row as it stood.
    [Fact]
    public async Task Save_OneStatement_TombstonedInsideItsTransactionBeforeItIsVisible()
    {
        var watcher = new CommitWatcher(Redis, BookKey, _file, _book);
        await using var host = await StartAsync(watcher, databaseFile: _file);
        (await host.GetAsync($"/api/v1/books/{_book}", Ours)).Dispose();

        await host.InScopeAsync(Ours, async database =>
        {
            (await database.Books.SingleAsync(row => row.Id == _book, TestContext.Current.CancellationToken)).Title = "Dune Messiah";
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);

            // Back as it was: the context opens transactions only where Entity Framework would.
            Assert.Equal(AutoTransactionBehavior.WhenNeeded, database.Database.AutoTransactionBehavior);
        });

        var seen = Assert.Single(watcher.Commits);
        Assert.Equal("1", seen.Gone);
        Assert.Equal("3", seen.Version);
        Assert.Equal("Dune", seen.TitleElsewhere);
        Assert.Equal("Dune Messiah", await TitleAsync(host));
    }

    [Fact]
    public async Task Save_AnotherWriterHoldingTheSameVersion_RefusedByTheConcurrencyToken()
    {
        await using var host = await StartAsync();

        await host.InScopeAsync(Ours, async first =>
        {
            using var scope = host.Services.CreateScope();
            var second = scope.ServiceProvider.GetRequiredService<ShelfContext>();
            var mine = await first.Books.SingleAsync(row => row.Id == _book, TestContext.Current.CancellationToken);
            var theirs = await second.Books.IgnoreQueryFilters().SingleAsync(row => row.Id == _book, TestContext.Current.CancellationToken);
            mine.Title = "Mine";
            theirs.Title = "Theirs";

            await first.SaveChangesAsync(TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(TestContext.Current.CancellationToken));
        });

        // One change made 3 dead and committed 4; the other committed nothing.
        Assert.Equal("3", (string?)await Redis.HashGetAsync(BookKey, OwnedCacheEntry.Version));
        Assert.Equal("Mine", await TitleAsync(host));
    }

    // The committed version is filled over the tombstone; a slow reader's dead version isn't.
    [Fact]
    public async Task Fill_AfterTheChange_CommittedVersionAcceptedAndTheDeadOneRefused()
    {
        await using var host = await StartAsync();
        var store = host.Services.GetRequiredService<IOwnedCacheStore>();
        (await host.GetAsync($"/api/v1/books/{_book}", Ours)).Dispose();
        await ChangeTitleAsync(host, "Children of Dune");

        Assert.False(await store.FillAsync(BookKey, 3, null, "{\"title\":\"Dune\"}", TimeSpan.FromMinutes(1)));
        (await host.GetAsync($"/api/v1/books/{_book}", Ours)).Dispose();

        Assert.Equal("4", (string?)await Redis.HashGetAsync(BookKey, OwnedCacheEntry.Version));
        Assert.Contains("Children of Dune", (string?)await Redis.HashGetAsync(BookKey, OwnedCacheEntry.Data));
    }

    // A tombstone left by a transaction that didn't commit marks the row's own version dead: reads of it are misses,
    // answered by the service, until the tombstone expires. Nothing stale is served.
    [Fact]
    public async Task Commit_FailedAfterTheTombstone_ReadsAreMissesAndNothingStale()
    {
        await using var host = await StartAsync(new FailingCommit());
        (await host.GetAsync($"/api/v1/books/{_book}", Ours)).Dispose();

        await Assert.ThrowsAsync<InvalidOperationException>(() => ChangeTitleAsync(host, "God Emperor of Dune"));

        using var response = await host.GetAsync($"/api/v1/books/{_book}", Ours);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"Dune\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("1", (string?)await Redis.HashGetAsync(BookKey, OwnedCacheEntry.Gone));
        Assert.Equal("3", (string?)await Redis.HashGetAsync(BookKey, OwnedCacheEntry.Version));
        Assert.Equal("Dune", await TitleAsync(host));
    }

    // A transaction commits through the interceptor of the context that began it, which knows nothing of what another
    // context saved in it: joining one is refused, so no change commits with the cache never told.
    [Fact]
    public async Task UseTransaction_AnotherContextsTransaction_Refused()
    {
        await using var host = await StartAsync();

        await host.InScopeAsync(Ours, async database =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            using var other = host.Services.CreateScope();
            var joining = other.ServiceProvider.GetRequiredService<ShelfContext>();

            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => joining.Database.UseTransactionAsync(
                Microsoft.EntityFrameworkCore.Storage.DbContextTransactionExtensions.GetDbTransaction(transaction), TestContext.Current.CancellationToken));

            Assert.Contains("can't join a transaction another context began", refusal.Message);
        });
    }

    [Fact]
    public async Task Fill_ReadLongerAgoThanTheFillLimit_Skipped()
    {
        await using var host = await StartAsync(settings: new Dictionary<string, string?> { ["Caching:Owned:FillLimit"] = "00:00:00" });

        using var response = await host.GetAsync($"/api/v1/books/{_book}", Ours);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await Redis.KeyExistsAsync(BookKey));
    }

    [Fact]
    public async Task Fill_WithinTheFillLimit_Filled()
    {
        await using var host = await StartAsync();

        (await host.GetAsync($"/api/v1/books/{_book}", Ours)).Dispose();

        Assert.Equal("3", (string?)await Redis.HashGetAsync(BookKey, OwnedCacheEntry.Version));
    }

    // An account's writes are last-writer-wins, so its version is no concurrency token: two changes from the same
    // version both commit. The database moves the version on under the row's lock, so each marks a different one dead.
    [Fact]
    public async Task Save_VersionNoTokenGuards_MovedOnInTheDatabaseOncePerChange()
    {
        var reader = Guid.NewGuid();
        var key = $"{ShelfHost.ServiceName}:reader:{reader}";
        await using var host = await ShelfHost.StartAsync(redis.Service, services =>
            services.AddQuery<IGetReaderQuery, GetReaderQuery>().AddOwnedCache<ShelfContext>());
        await host.InScopeAsync(Ours, async database =>
        {
            database.Readers.Add(new ReaderEntity { Id = reader, OrganizationId = Ours, Name = "Paul", CacheVersion = 1 });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        await host.InScopeAsync(Ours, async first =>
        {
            using var scope = host.Services.CreateScope();
            var second = scope.ServiceProvider.GetRequiredService<ShelfContext>();
            var mine = await first.Readers.SingleAsync(row => row.Id == reader, TestContext.Current.CancellationToken);
            var theirs = await second.Readers.IgnoreQueryFilters().SingleAsync(row => row.Id == reader, TestContext.Current.CancellationToken);
            mine.Name = "Muad'Dib";
            theirs.Name = "Usul";

            await first.SaveChangesAsync(TestContext.Current.CancellationToken);
            await second.SaveChangesAsync(TestContext.Current.CancellationToken);

            // Each context holds the version its own change moved the row to.
            Assert.Equal(2, mine.CacheVersion);
            Assert.Equal(3, theirs.CacheVersion);
        });

        var stored = 0;
        await host.InScopeAsync(Ours, async database =>
            stored = (await database.Readers.AsNoTracking().SingleAsync(row => row.Id == reader, TestContext.Current.CancellationToken)).CacheVersion);
        Assert.Equal(3, stored);
        Assert.Equal("2", (string?)await Redis.HashGetAsync(key, OwnedCacheEntry.Version));

        // The first change's 2, which a reader could have filled between the commits, is dead; the second's 3 isn't.
        var store = host.Services.GetRequiredService<IOwnedCacheStore>();
        Assert.False(await store.FillAsync(key, 2, null, "{\"name\":\"Muad'Dib\"}", TimeSpan.FromMinutes(1)));
        Assert.True(await store.FillAsync(key, 3, null, "{\"name\":\"Usul\"}", TimeSpan.FromMinutes(1)));
    }

    private async Task<ShelfHost> StartAsync(
        IInterceptor? after = null,
        IReadOnlyDictionary<string, string?>? settings = null,
        string? databaseFile = null)
    {
        var host = await ShelfHost.StartAsync(
            redis.Service,
            services =>
            {
                services.AddQuery<IGetBookQuery, GetBookQuery>().AddOwnedCache<ShelfContext>();

                // After the cache's own interceptors: sees each commit once the cache has been told.
                if (after is not null)
                {
                    services.ConfigureDbContext<ShelfContext>(options => options.AddInterceptors(after));
                }
            },
            settings: settings,
            databaseFile: databaseFile);

        await host.InScopeAsync(Ours, async database =>
        {
            database.Books.Add(new BookEntity { Id = _book, OrganizationId = Ours, Title = "Dune", Version = 3 });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        return host;
    }

    private Task ChangeTitleAsync(ShelfHost host, string title) =>
        host.InScopeAsync(Ours, async database =>
        {
            (await database.Books.SingleAsync(row => row.Id == _book, TestContext.Current.CancellationToken)).Title = title;
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

    private async Task<string> TitleAsync(ShelfHost host)
    {
        var title = string.Empty;
        await host.InScopeAsync(Ours, async database =>
            title = (await database.Books.AsNoTracking().SingleAsync(row => row.Id == _book, TestContext.Current.CancellationToken)).Title);
        return title;
    }

    /// <summary>
    /// What a commit of the book saw as it was about to happen.
    /// </summary>
    private sealed record Seen(string? Gone, string? Version, string? TitleElsewhere);

    /// <summary>
    /// At each commit, once the cache's interceptor has run: the book's entry, and its title as another connection
    /// reads it.
    /// </summary>
    private sealed class CommitWatcher(IDatabase redis, string key, string file, Guid book) : DbTransactionInterceptor
    {
        public List<Seen> Commits { get; } = [];

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            // The seed's own commit, before anything was read, has no entry to look at.
            if (!await redis.KeyExistsAsync(key))
            {
                return result;
            }

            await using var elsewhere = new SqliteConnection($"DataSource={file}");
            await elsewhere.OpenAsync(cancellationToken);
            await using var read = elsewhere.CreateCommand();
            read.CommandText = "SELECT Title FROM Books WHERE Id = $id";
            read.Parameters.AddWithValue("$id", book.ToString().ToUpperInvariant());

            Commits.Add(new Seen(
                await redis.HashGetAsync(key, OwnedCacheEntry.Gone),
                await redis.HashGetAsync(key, OwnedCacheEntry.Version),
                (string?)await read.ExecuteScalarAsync(cancellationToken)));
            return result;
        }
    }

    /// <summary>
    /// A commit that fails once the cache has been told: the change is never made.
    /// </summary>
    private sealed class FailingCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            eventData.Context?.ChangeTracker.Entries<BookEntity>().Any(entry => entry.Entity.Title != "Dune") == true
                ? throw new InvalidOperationException("The commit failed.")
                : ValueTask.FromResult(result);
    }
}
