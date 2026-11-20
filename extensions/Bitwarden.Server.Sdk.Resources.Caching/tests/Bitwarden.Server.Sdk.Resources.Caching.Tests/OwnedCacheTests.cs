using System.Net;
using System.Text.Json;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Data;
using Bitwarden.Server.Sdk.Resources.Querying;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Resources.Caching.Tests;

/// <summary>
/// The service side of the service-owned cache: written on read, invalidated just before commit with the version the change
/// makes dead, never older than what committed.
/// </summary>
public sealed class OwnedCacheTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private static readonly Guid Ours = Guid.NewGuid();
    private static readonly Guid Theirs = Guid.NewGuid();

    private readonly Guid _book = Guid.NewGuid();
    private ConnectionMultiplexer _redis = null!;

    private IDatabase Redis => _redis.GetDatabase();

    private string BookKey => $"{ShelfHost.ServiceName}:book:{_book}";

    public async ValueTask InitializeAsync() =>
        _redis = await ConnectionMultiplexer.ConnectAsync(redis.Hostname);

    public async ValueTask DisposeAsync() => await _redis.DisposeAsync();

    [Fact]
    public async Task Get_CacheableRead_FillsTheWholeResourceTheOwnerTheVersionAndTheFormat()
    {
        await using var host = await StartAsync();

        using var response = await host.GetAsync($"/api/v1/books/{_book}", Ours);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = await Entry(BookKey);
        Assert.Equal("3", entry[OwnedCacheEntry.Version]);
        Assert.Equal($$"""{"organization":"{{Ours}}"}""", entry[OwnedCacheEntry.Owner]);
        Assert.Equal("1", entry[OwnedCacheEntry.Format]);

        using var data = JsonDocument.Parse(entry[OwnedCacheEntry.Data]);
        Assert.Equal(_book.ToString(), data.RootElement.GetProperty("id").GetString());
        Assert.Equal("book", data.RootElement.GetProperty("type").GetString());
        var attributes = data.RootElement.GetProperty("attributes");
        Assert.Equal("Dune", attributes.GetProperty("title").GetString());
        Assert.Equal("3", attributes.GetProperty("version").GetString());
        Assert.False(attributes.TryGetProperty("shelfmark", out _));

        // The service's own answer is whole: what it works out per caller is never cached, and the caller still gets it.
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("A-1", body.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("shelfmark").GetString());
    }

    [Fact]
    public async Task Get_AnotherOrganizationsRow_NotFoundAndNothingCached()
    {
        await using var host = await StartAsync();

        using var response = await host.GetAsync($"/api/v1/books/{_book}", Theirs);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(await Redis.KeyExistsAsync(BookKey));
    }

    [Fact]
    public async Task Save_ChangedRow_MarksTheVersionItReplacedDead()
    {
        await using var host = await StartAsync();
        (await host.GetAsync($"/api/v1/books/{_book}", Ours)).Dispose();

        await host.InScopeAsync(Ours, async database =>
        {
            var book = await database.Books.SingleAsync(row => row.Id == _book, TestContext.Current.CancellationToken);
            book.Title = "Dune Messiah";

            // The version is left alone, as a careless write would leave it.
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
            Assert.Equal(4, book.Version);
        });

        var entry = await Entry(BookKey);
        Assert.Equal("3", entry[OwnedCacheEntry.Version]);
        Assert.Equal("1", entry[OwnedCacheEntry.Gone]);
        Assert.False(entry.ContainsKey(OwnedCacheEntry.Data));
    }

    [Fact]
    public async Task Get_AfterTheChange_FillsTheNewVersionOverItsTombstone()
    {
        await using var host = await StartAsync();
        await host.InScopeAsync(Ours, async database =>
        {
            (await database.Books.SingleAsync(row => row.Id == _book, TestContext.Current.CancellationToken)).Title = "Children of Dune";
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        (await host.GetAsync($"/api/v1/books/{_book}", Ours)).Dispose();

        var entry = await Entry(BookKey);
        Assert.Equal("4", entry[OwnedCacheEntry.Version]);
        Assert.Contains("Children of Dune", entry[OwnedCacheEntry.Data]);
    }

    [Fact]
    public async Task Fill_TheDeadVersion_RefusedAndTheCommittedVersionAccepted()
    {
        await using var host = await StartAsync();
        var store = host.Services.GetRequiredService<IOwnedCacheStore>();
        var key = $"{ShelfHost.ServiceName}:book:{Guid.NewGuid()}";

        Assert.True(await store.FillAsync(key, 6, null, "{\"six\":true}", TimeSpan.FromMinutes(1)));
        await store.InvalidateAsync([new Invalidation(key, 6)], TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        // The slow reader who read 6 before 7 committed, and one older still.
        Assert.False(await store.FillAsync(key, 6, null, "{\"six\":true}", TimeSpan.FromMinutes(1)));
        Assert.False(await store.FillAsync(key, 5, null, "{\"five\":true}", TimeSpan.FromMinutes(1)));
        Assert.True(await store.FillAsync(key, 7, null, "{\"seven\":true}", TimeSpan.FromMinutes(1)));

        // Marking 6 dead again keeps the committed 7.
        await store.InvalidateAsync([new Invalidation(key, 6)], TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        Assert.Equal("{\"seven\":true}", (await Entry(key))[OwnedCacheEntry.Data]);
    }

    // An arrival's tombstone lives the shorter lifetime it is given, but never shortens one already there: a deletion's
    // still has fills to refuse after the arrival's would have gone.
    [Fact]
    public async Task Invalidate_Arrival_LivesItsOwnLifetimeAndNeverShortensATombstoneThere()
    {
        await using var host = await StartAsync();
        var store = host.Services.GetRequiredService<IOwnedCacheStore>();
        var arrived = $"{ShelfHost.ServiceName}:book:{Guid.NewGuid()}";
        var deleted = $"{ShelfHost.ServiceName}:book:{Guid.NewGuid()}";
        await store.InvalidateAsync([new Invalidation(deleted, 6)], TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30));

        await store.InvalidateAsync(
            [new Invalidation(arrived, Invalidation.EveryVersion, Arrival: true), new Invalidation(deleted, Invalidation.EveryVersion, Arrival: true)],
            TimeSpan.FromMinutes(10),
            TimeSpan.FromSeconds(30));

        Assert.InRange((await Redis.KeyTimeToLiveAsync(arrived))!.Value, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
        Assert.Equal(Invalidation.EveryVersion.ToString(), (await Entry(deleted))[OwnedCacheEntry.Version]);
        Assert.True((await Redis.KeyTimeToLiveAsync(deleted))!.Value > TimeSpan.FromMinutes(9));
    }

    [Fact]
    public async Task Invalidate_AnOlderDeadVersion_KeepsTheTombstoneOfANewerOne()
    {
        await using var host = await StartAsync();
        var store = host.Services.GetRequiredService<IOwnedCacheStore>();
        var key = $"{ShelfHost.ServiceName}:book:{Guid.NewGuid()}";
        await store.InvalidateAsync([new Invalidation(key, 8)], TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        await store.InvalidateAsync([new Invalidation(key, 6)], TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        Assert.Equal("8", (await Entry(key))[OwnedCacheEntry.Version]);
        Assert.False(await store.FillAsync(key, 7, null, "{\"seven\":true}", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Fill_TheSameRowAgain_LeavesTheEntryAlone()
    {
        await using var host = await StartAsync();
        var store = host.Services.GetRequiredService<IOwnedCacheStore>();
        var key = $"{ShelfHost.ServiceName}:book:{Guid.NewGuid()}";

        Assert.True(await store.FillAsync(key, 6, "{\"organization\":\"o\"}", "{\"six\":true}", TimeSpan.FromMinutes(1)));
        await Redis.HashSetAsync(key, "marker", "1");

        // The service serving the same row again: nothing is rewritten.
        Assert.True(await store.FillAsync(key, 6, "{\"organization\":\"o\"}", "{\"six\":true}", TimeSpan.FromMinutes(1)));
        Assert.Equal("1", (await Entry(key))["marker"]);

        // Different bytes at the same version: the entry written first stands until its row moves on.
        Assert.False(await store.FillAsync(key, 6, "{\"organization\":\"o\"}", "{\"six\":2}", TimeSpan.FromMinutes(1)));
        Assert.Equal("1", (await Entry(key))["marker"]);
        Assert.Equal("{\"six\":true}", (await Entry(key))[OwnedCacheEntry.Data]);
    }

    [Fact]
    public async Task Fill_TheVersionAfterTheDeadOne_ReplacesTheTombstone()
    {
        await using var host = await StartAsync();
        var store = host.Services.GetRequiredService<IOwnedCacheStore>();
        var key = $"{ShelfHost.ServiceName}:book:{Guid.NewGuid()}";
        await store.InvalidateAsync([new Invalidation(key, 7)], TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        Assert.False(await store.FillAsync(key, 7, null, "{\"seven\":true}", TimeSpan.FromMinutes(1)));
        Assert.True(await store.FillAsync(key, 8, null, "{\"eight\":true}", TimeSpan.FromMinutes(1)));

        var entry = await Entry(key);
        Assert.False(entry.ContainsKey(OwnedCacheEntry.Gone));
        Assert.Equal("{\"eight\":true}", entry[OwnedCacheEntry.Data]);
    }

    [Fact]
    public async Task Save_DeletedRow_MarksItsLastVersionDead()
    {
        await using var host = await StartAsync();
        (await host.GetAsync($"/api/v1/books/{_book}", Ours)).Dispose();

        await host.InScopeAsync(Ours, async database =>
        {
            database.Books.Remove(await database.Books.SingleAsync(row => row.Id == _book, TestContext.Current.CancellationToken));
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        var entry = await Entry(BookKey);
        Assert.Equal("3", entry[OwnedCacheEntry.Version]);
        Assert.Equal("1", entry[OwnedCacheEntry.Gone]);
    }

    [Fact]
    public async Task SaveInATransaction_TombstonedOnlyAsItCommits()
    {
        await using var host = await StartAsync();
        (await host.GetAsync($"/api/v1/books/{_book}", Ours)).Dispose();

        await host.InScopeAsync(Ours, async database =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            (await database.Books.SingleAsync(row => row.Id == _book, TestContext.Current.CancellationToken)).Title = "Heretics of Dune";
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);

            Assert.False((await Entry(BookKey)).ContainsKey(OwnedCacheEntry.Gone));

            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        });

        Assert.Equal("1", (await Entry(BookKey))[OwnedCacheEntry.Gone]);
    }

    // Under snapshot or repeatable-read isolation a row read in a transaction can be as old as the transaction.
    [Fact]
    public async Task ReadInATransaction_AnsweredAndNothingFilled()
    {
        await using var host = await StartAsync();
        await Redis.KeyDeleteAsync(BookKey);

        using (var scope = host.Services.CreateScope())
        using (ContextScope.Begin(new StatedContext { OrganizationId = Ours }))
        {
            var database = scope.ServiceProvider.GetRequiredService<ShelfContext>();
            var query = scope.ServiceProvider.GetRequiredService<IGetBookQuery>();
            await using var transaction = await database.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

            var read = await query.ReadAsync(_book.ToString(), default, TestContext.Current.CancellationToken);

            Assert.Equal(_book.ToString(), read.Data!.Id);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        Assert.False(await Redis.KeyExistsAsync(BookKey));
        (await host.GetAsync($"/api/v1/books/{_book}", Ours)).Dispose();
        Assert.True(await Redis.KeyExistsAsync(BookKey));
    }

    // A change saved in a transaction is invalidated only as it commits: until then the entry is older than the change.
    [Fact]
    public async Task ReadInATransaction_AfterItsOwnSave_AnswersTheChangeNotTheEntry()
    {
        await using var host = await StartAsync();
        (await host.GetAsync($"/api/v1/books/{_book}", Ours)).Dispose();
        Assert.True(await Redis.KeyExistsAsync(BookKey));

        using var scope = host.Services.CreateScope();
        using var context = ContextScope.Begin(new StatedContext { OrganizationId = Ours });
        var database = scope.ServiceProvider.GetRequiredService<ShelfContext>();
        var query = scope.ServiceProvider.GetRequiredService<IGetBookQuery>();
        await using var transaction = await database.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        (await database.Books.SingleAsync(row => row.Id == _book, TestContext.Current.CancellationToken)).Title = "Heretics of Dune";
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        var read = await query.ReadAsync(_book.ToString(), new Fields<Book>(["title"]), TestContext.Current.CancellationToken);

        Assert.Equal("Heretics of Dune", read.Data!.Title);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Erasure_ErasedRows_EveryVersionDeadAsTheErasureCommits()
    {
        await using var host = await StartAsync();
        (await host.GetAsync($"/api/v1/books/{_book}", Ours)).Dispose();

        await host.InScopeAsync(null, async database =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await OwnerErasure.EraseAsync(database, Owner.Organization, Ours, TestContext.Current.CancellationToken);
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        });

        // A delete in bulk checks no version, so every version is dead: nothing of the row is filled again.
        var entry = await Entry(BookKey);
        Assert.Equal("1", entry[OwnedCacheEntry.Gone]);
        Assert.Equal(Invalidation.EveryVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), entry[OwnedCacheEntry.Version]);
    }

    // Outside a transaction each delete commits as it runs: the cache is told before it does.
    [Fact]
    public async Task Erasure_OutsideATransaction_TombstonesBeforeTheDelete()
    {
        await using var host = await StartAsync();
        (await host.GetAsync($"/api/v1/books/{_book}", Ours)).Dispose();

        await host.InScopeAsync(null, async database =>
            await OwnerErasure.EraseAsync(database, Owner.Organization, Ours, TestContext.Current.CancellationToken));

        Assert.Equal("1", (await Entry(BookKey))[OwnedCacheEntry.Gone]);
    }

    [Fact]
    public async Task ReadByKey_NothingRecorded_CachedAsNothingBelowEveryVersion()
    {
        await using var host = await StartAsync();
        var key = $"{ShelfHost.ServiceName}:quota:by-organization:{Theirs}";
        await Redis.KeyDeleteAsync(key);

        using var response = await host.GetAsync($"/api/v1/quotas/{Theirs}", Theirs);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var entry = await Entry(key);
        Assert.Equal("-1", entry[OwnedCacheEntry.Version]);
        Assert.Equal("1", entry[OwnedCacheEntry.None]);
        Assert.Equal($$"""{"organization":"{{Theirs}}"}""", entry[OwnedCacheEntry.Owner]);

        await host.InScopeAsync(Theirs, async database =>
        {
            database.Quotas.Add(new QuotaEntity { Id = Guid.NewGuid(), OrganizationId = Theirs, Limit = 2, Version = 1 });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        // "Nothing recorded" is dead; the row that arrived is filled over it.
        Assert.Equal("1", (await Entry(key))[OwnedCacheEntry.Gone]);
        Assert.Equal("-1", (await Entry(key))[OwnedCacheEntry.Version]);
        (await host.GetAsync($"/api/v1/quotas/{Theirs}", Theirs)).Dispose();
        Assert.Equal("1", (await Entry(key))[OwnedCacheEntry.Version]);
        Assert.False((await Entry(key)).ContainsKey(OwnedCacheEntry.Gone));
    }

    [Fact]
    public async Task Contract_CacheableRead_PublishesItsDeclaration()
    {
        await using var host = await StartAsync();

        using var response = await host.Client().GetAsync("/api/openapi.json", TestContext.Current.CancellationToken);
        using var contract = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var cache = contract.RootElement.GetProperty("paths").GetProperty("/api/v1/books/{id}").GetProperty("get")
            .GetProperty(OwnedCacheEntry.Extension);
        Assert.Equal("book", cache.GetProperty("resource").GetString());
        Assert.Equal("shelf-service:book:{key}", cache.GetProperty("key").GetString());
        Assert.Equal("id", cache.GetProperty("keyedBy").GetString());
        Assert.Equal(ShelfHost.ReadScope, cache.GetProperty("scope").GetString());
        Assert.Equal("required", cache.GetProperty("context").GetProperty("organization").GetString());
        Assert.Equal("organization", cache.GetProperty("owner").GetProperty("axis").GetString());
        Assert.False(cache.GetProperty("owner").GetProperty("self").GetBoolean());
        Assert.False(cache.GetProperty("owner").GetProperty("acrossOwners").GetBoolean());
        var fields = cache.GetProperty("fields").EnumerateArray().Select(field => field.GetString()).ToList();
        Assert.Contains("title", fields);
        Assert.Contains("inscription", fields);
        Assert.DoesNotContain("shelfmark", fields);
        Assert.Equal(["shelfmark"], cache.GetProperty("perCaller").EnumerateArray().Select(field => field.GetString()));
        Assert.Equal(OwnedCacheEntry.CurrentFormat, cache.GetProperty("format").GetInt32());

        var quotas = contract.RootElement.GetProperty("paths").GetProperty("/api/v1/quotas/{organization}").GetProperty("get")
            .GetProperty(OwnedCacheEntry.Extension);
        Assert.Equal("organization", quotas.GetProperty("keyedBy").GetString());
        Assert.True(quotas.GetProperty("owner").GetProperty("acrossOwners").GetBoolean());
    }

    [Fact]
    public async Task ClientFromThePublishedContract_ReadsWhatTheServiceFilled()
    {
        await using var host = await StartAsync();
        (await host.GetAsync($"/api/v1/books/{_book}", Ours)).Dispose();
        using var response = await host.Client().GetAsync("/api/openapi.json", TestContext.Current.CancellationToken);
        var contract = Client.CacheableRead.FromContract(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));

        var context = new StatedContext { OrganizationId = Ours };
        var services = new ServiceCollection()
            .AddSingleton<IContext>(context)
            .Configure<Client.BitwardenClientOptions>(ShelfHost.ServiceName, options => options.Scopes = [ShelfHost.ReadScope])
            .BuildServiceProvider();
        // On a clock that stands still, so the read waits for Redis however long a busy machine
        // takes, rather than only Client.CachedReads.Patience, and the answer is the cache's.
        await using var connection = new Client.CachedReadsConnection(redis.Hostname, new FakeTimeProvider());
        var reads = new Client.CachedReads<OwnedCacheTests>(
            ShelfHost.ServiceName,
            contract,
            connection,
            services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Client.BitwardenClientOptions>>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Client.CachedReads<OwnedCacheTests>>.Instance);

        var answer = await reads.ReadAsync("getBook", _book, ["title"], TestContext.Current.CancellationToken);

        Assert.Equal(Client.CachedAnswerKind.Hit, answer.Kind);
        Assert.Contains("\"title\":\"Dune\"", answer.Resource);

        // A field the service computes per caller is never answered from the cache, nor is every field.
        Assert.Equal(Client.CachedAnswerKind.Miss, (await reads.ReadAsync("getBook", _book, ["title", "shelfmark"], TestContext.Current.CancellationToken)).Kind);
        Assert.Equal(Client.CachedAnswerKind.Miss, (await reads.ReadAsync("getBook", _book, [], TestContext.Current.CancellationToken)).Kind);
    }

    [Fact]
    public async Task CachingOff_ReadsTheDatabaseAndWritesNothing()
    {
        await using var host = await StartAsync(withRedis: false);

        using var response = await host.GetAsync($"/api/v1/books/{_book}", Ours);
        await host.InScopeAsync(Ours, async database =>
        {
            (await database.Books.SingleAsync(row => row.Id == _book, TestContext.Current.CancellationToken)).Title = "Chapterhouse";
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
            Assert.Equal(3, (await database.Books.AsNoTracking().SingleAsync(row => row.Id == _book, TestContext.Current.CancellationToken)).Version);
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await Redis.KeyExistsAsync(BookKey));
    }

    // Outside Development, entries only the owner may write need a Redis user of the owner's own.
    [Theory]
    [InlineData("")]
    [InlineData(",user=default")]
    public async Task Start_WithACacheButNoUserOfItsOwn_OutsideDevelopment_Refused(string user)
    {
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => ShelfHost.StartAsync(
            redis.Hostname + user,
            services => services.AddQuery<IGetBookQuery, GetBookQuery>().AddOwnedCache<ShelfContext>()));

        Assert.Contains(OwnedCacheEntry.ConnectionKey, refusal.Message);
        Assert.DoesNotContain(redis.Hostname, refusal.Message);
    }

    [Fact]
    public async Task Start_WithACacheButNoUser_InDevelopment_Starts()
    {
        await using var host = await ShelfHost.StartAsync(
            redis.Hostname,
            services => services.AddQuery<IGetBookQuery, GetBookQuery>().AddOwnedCache<ShelfContext>(),
            environmentName: "Development");

        Assert.NotNull(host.Services.GetRequiredService<IOwnedCacheStore>());
    }

    private async Task<ShelfHost> StartAsync(bool withRedis = true)
    {
        var host = await ShelfHost.StartAsync(withRedis ? redis.Service : null, services => services
            .AddQuery<IGetBookQuery, GetBookQuery>()
            .AddQuery<IGetQuotaQuery, GetQuotaQuery>()
            .AddOwnedCache<ShelfContext>());

        await host.InScopeAsync(Ours, async database =>
        {
            database.Books.Add(new BookEntity { Id = _book, OrganizationId = Ours, Title = "Dune", Version = 3 });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        return host;
    }

    private async Task<Dictionary<string, string>> Entry(string key) =>
        (await Redis.HashGetAllAsync(key)).ToDictionary(field => field.Name.ToString(), field => field.Value.ToString());
}
