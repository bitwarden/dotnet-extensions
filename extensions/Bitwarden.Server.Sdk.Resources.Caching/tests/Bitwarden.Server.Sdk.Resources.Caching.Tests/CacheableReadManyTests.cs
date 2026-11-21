using System.Data.Common;
using System.Net;
using System.Text.Json;
using Bitwarden.Server.Sdk.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Resources.Caching.Tests;

/// <summary>
/// A cached read-many: a borrower's open loans across organizations, kept as one entry owned by the borrower, filled on
/// read, paged from, and marked dead at every version by any change to one of the borrower's loans, just before it
/// commits.
/// </summary>
public sealed partial class CacheableReadManyTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private const string Path = "/api/v1/open-loans";

    private const string Fields = "?fields[open-loan]=title,organization,borrower";

    private const string Unreachable = "127.0.0.1:1,abortConnect=false,connectTimeout=200,syncTimeout=500,asyncTimeout=500,user=shelf-service,password=unused";

    // A commit allowance of two seconds, which the startup check accepts with a command timeout of one second and a
    // fill limit of half of one.
    private static readonly Dictionary<string, string?> ShortAllowance = new()
    {
        [ShelfHost.CommandTimeoutKey] = "1",
        ["Caching:Owned:FillLimit"] = "00:00:00.5",
        ["Caching:Owned:CommitAllowance"] = "00:00:02",
    };

    private readonly Guid _library = Guid.NewGuid();
    private readonly Guid _branch = Guid.NewGuid();
    private readonly Guid _borrower = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();
    private readonly Guid _first = Guid.NewGuid();
    private readonly Guid _second = Guid.NewGuid();
    private readonly Guid _returned = Guid.NewGuid();
    private readonly Guid _emma = Guid.NewGuid();
    private ConnectionMultiplexer _redis = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IDatabase Redis => _redis.GetDatabase();

    private static string ListKey(Guid borrower) => $"{ShelfHost.ServiceName}:open-loan:read-many:{borrower}";

    public async ValueTask InitializeAsync() => _redis = await ConnectionMultiplexer.ConnectAsync(redis.Hostname);

    public async ValueTask DisposeAsync() => await _redis.DisposeAsync();

    [Fact]
    public async Task Get_ReadMany_FillsTheWholeListInItsOrderOwnedByTheBorrower()
    {
        await using var host = await StartAsync();

        using var response = await host.GetAsync($"{Path}{Fields}", null, _borrower);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([_first.ToString(), _second.ToString()], await IdsAsync(response));
        var entry = await Entry(ListKey(_borrower));
        Assert.Equal("0", entry[OwnedCacheEntry.Version]);
        Assert.Equal($$"""{"user":"{{_borrower}}"}""", entry[OwnedCacheEntry.Owner]);
        Assert.Equal("1", entry[OwnedCacheEntry.Format]);
        using var data = JsonDocument.Parse(entry[OwnedCacheEntry.Data]);
        Assert.Equal(
            [_first.ToString(), _second.ToString()],
            data.RootElement.EnumerateArray().Select(loan => loan.GetProperty("id").GetString()));
        Assert.Equal("Anathem", data.RootElement[0].GetProperty("attributes").GetProperty("title").GetString());
    }

    [Fact]
    public async Task Get_ReadMany_FromTheEntry_IsTheSameResponseAsFromTheDatabase()
    {
        await using var host = await StartAsync();

        var fromDatabase = await BodyAsync(host, _borrower);
        Assert.True(await Redis.KeyExistsAsync(ListKey(_borrower)));
        var fromEntry = await BodyAsync(host, _borrower);

        Assert.Equal(fromDatabase, fromEntry);
    }

    // The entry answers the owner's own read: tampered behind its back, as only a bug would, it comes back.
    [Fact]
    public async Task Get_ReadMany_Warm_AnsweredFromTheEntry()
    {
        await using var host = await StartAsync();
        (await host.GetAsync($"{Path}{Fields}", null, _borrower)).Dispose();
        await Redis.HashSetAsync(ListKey(_borrower), OwnedCacheEntry.Data, "[]");

        using var response = await host.GetAsync($"{Path}{Fields}", null, _borrower);

        Assert.Empty(await IdsAsync(response));
    }

    // Another borrower's entry is never theirs: its owner is checked, as a client checks it.
    [Fact]
    public async Task Get_ReadMany_AnEntryOwnedBySomebodyElse_NotAnswered()
    {
        await using var host = await StartAsync();
        (await host.GetAsync($"{Path}{Fields}", null, _borrower)).Dispose();
        await Redis.HashSetAsync(ListKey(_borrower), OwnedCacheEntry.Owner, $$"""{"user":"{{_other}}"}""");

        using var response = await host.GetAsync($"{Path}{Fields}", null, _borrower);

        Assert.Equal([_first.ToString(), _second.ToString()], await IdsAsync(response));
    }

    public enum Change
    {
        Returned,
        Deleted,
        Added,
        Renamed,
        MovedAway,
        ReturnedLoanRenewed,
    }

    // Whatever the change to one of the borrower's loans, before or after it, every version of the entry is dead until
    // the commit allowance has passed, and the next read answers the change.
    [Theory]
    [InlineData(Change.Returned)]
    [InlineData(Change.Deleted)]
    [InlineData(Change.Added)]
    [InlineData(Change.Renamed)]
    [InlineData(Change.MovedAway)]
    [InlineData(Change.ReturnedLoanRenewed)]
    public async Task Change_AnyLoanOfTheBorrower_EveryVersionDeadAndTheNextReadAnswersIt(Change change)
    {
        await using var host = await StartAsync(settings: ShortAllowance);
        var before = await IdsAsync(await host.GetAsync($"{Path}{Fields}", null, _borrower));
        var added = Guid.NewGuid();

        await host.InScopeAsync(change is Change.Added or Change.ReturnedLoanRenewed ? _library : _branch, async database =>
        {
            switch (change)
            {
                case Change.Returned:
                    (await database.Loans.SingleAsync(row => row.Id == _first, Ct)).Returned = true;
                    break;
                case Change.Deleted:
                    database.Loans.Remove(await database.Loans.SingleAsync(row => row.Id == _first, Ct));
                    break;
                case Change.Added:
                    database.Loans.Add(new LoanEntity { Id = added, OrganizationId = _library, Borrower = _borrower, Title = "Zodiac", Version = 1 });
                    break;
                case Change.Renamed:
                    (await database.Loans.SingleAsync(row => row.Id == _first, Ct)).Title = "Zodiac";
                    break;
                case Change.MovedAway:
                    (await database.Loans.SingleAsync(row => row.Id == _first, Ct)).Borrower = _other;
                    break;
                case Change.ReturnedLoanRenewed:
                    (await database.Loans.SingleAsync(row => row.Id == _returned, Ct)).Returned = false;
                    break;
            }

            await database.SaveChangesAsync(Ct);
        });

        var entry = await Entry(ListKey(_borrower));
        Assert.Equal("1", entry[OwnedCacheEntry.Gone]);
        Assert.Equal(Invalidation.EveryVersion.ToString(), entry[OwnedCacheEntry.Version]);
        Assert.InRange((await Redis.KeyTimeToLiveAsync(ListKey(_borrower)))!.Value, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(2));

        if (change == Change.MovedAway)
        {
            Assert.Equal("1", (await Entry(ListKey(_other)))[OwnedCacheEntry.Gone]);
        }

        var after = await IdsAsync(await host.GetAsync($"{Path}{Fields}", null, _borrower));
        string[] expected = change switch
        {
            Change.Returned or Change.Deleted or Change.MovedAway => [_second.ToString()],
            Change.Added => [.. before, added.ToString()],
            Change.Renamed => [_second.ToString(), _first.ToString()],
            _ => [_first.ToString(), _second.ToString(), _returned.ToString()],
        };
        Assert.Equal(expected, after);

        // Answered by the database while the tombstone lives, and filled again once it has gone.
        Assert.Equal("1", (await Entry(ListKey(_borrower)))[OwnedCacheEntry.Gone]);
        await WaitGoneAsync(ListKey(_borrower));
        Assert.Equal(expected, await IdsAsync(await host.GetAsync($"{Path}{Fields}", null, _borrower)));
        Assert.False((await Entry(ListKey(_borrower))).ContainsKey(OwnedCacheEntry.Gone));
    }

    // The race the tombstone closes: a read takes the rows, a change commits, and the read then fills what it took. The
    // fill is refused, and the next read answers the change.
    [Fact]
    public async Task Fill_AChangeCommittedBetweenTheReadAndTheFill_Refused()
    {
        var between = new BetweenReadAndFill();
        await using var host = await StartAsync(between: between);
        // Another request's change, in a flow of its own rather than the reading request's.
        between.Then = () =>
        {
            using (ExecutionContext.SuppressFlow())
            {
                return Task.Run(() => host.InScopeAsync(_branch, async database =>
                {
                    (await database.Loans.SingleAsync(row => row.Id == _first, Ct)).Returned = true;
                    await database.SaveChangesAsync(Ct);
                }));
            }
        };

        var raced = await IdsAsync(await host.GetAsync($"{Path}{Fields}", null, _borrower));

        Assert.Equal([_first.ToString(), _second.ToString()], raced);
        Assert.Equal("1", (await Entry(ListKey(_borrower)))[OwnedCacheEntry.Gone]);
        Assert.Equal([_second.ToString()], await IdsAsync(await host.GetAsync($"{Path}{Fields}", null, _borrower)));
    }

    // At the store: once a change has marked every version dead, no fill of a read-many is written until the tombstone goes.
    [Fact]
    public async Task Fill_AfterTheTombstone_RefusedUntilItExpires()
    {
        await using var host = await StartAsync();
        var store = host.Services.GetRequiredService<IOwnedCacheStore>();
        var key = ListKey(Guid.NewGuid());

        Assert.True(await store.FillAsync(key, 0, null, "[]", TimeSpan.FromMinutes(1)));
        await store.InvalidateAsync([new Invalidation(key, Invalidation.EveryVersion, Arrival: true)], TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(1));

        Assert.False(await store.FillAsync(key, 0, null, "[]", TimeSpan.FromMinutes(1)));
        await WaitGoneAsync(key);
        Assert.True(await store.FillAsync(key, 0, null, "[]", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Change_CacheUnreachable_Answers503AndCommitsNothing()
    {
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"shelf-{Guid.NewGuid():N}.db");

        try
        {
            // Seeded without a cache, then served with one that can't be reached.
            await using (var seeding = await ShelfHost.StartAsync(null, ListOnly, databaseFile: file))
            {
                await SeedAsync(seeding, cache: false);
            }

            await using var host = await ShelfHost.StartAsync(Unreachable, ListOnly, databaseFile: file);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/loans/{_first}/actions/return");
            request.Headers.Authorization = new("Bearer", ShelfHost.Token("shelves.write"));
            request.Headers.Add(BitwardenHeaders.Organization, _branch.ToString());
            request.Headers.Add(BitwardenHeaders.Delegation, ShelfHost.SignedContextToken(_branch, null));
            using var response = await host.Client().SendAsync(request, Ct);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            await host.InScopeAsync(_branch, async database =>
                Assert.False((await database.Loans.SingleAsync(row => row.Id == _first, Ct)).Returned));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(file);
        }

        static void ListOnly(IServiceCollection services) =>
            services.AddQuery<IListOpenLoans, OpenLoansList>().AddOwnedCache<ShelfContext>();
    }

    [Fact]
    public async Task ExecuteUpdate_OnTheTable_Refused()
    {
        await using var host = await StartAsync();

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => host.InScopeAsync(_branch, async database =>
            await database.Loans.Where(row => row.Id == _first)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Returned, true), Ct)));

        Assert.Contains("ExecuteUpdate on LoanEntity is refused", refusal.Message);
    }

    [Fact]
    public async Task Erasure_TheOrganizationsLoans_TombstonesTheBorrowersEntry()
    {
        await using var host = await StartAsync();
        (await host.GetAsync($"{Path}{Fields}", null, _borrower)).Dispose();

        await host.InScopeAsync(null, async database =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Ct);
            await Bitwarden.Server.Sdk.Data.OwnerErasure.EraseAsync(database, Bitwarden.Server.Sdk.Data.Owner.Organization, _branch, Ct);
            await transaction.CommitAsync(Ct);
        });

        Assert.Equal("1", (await Entry(ListKey(_borrower)))[OwnedCacheEntry.Gone]);
    }

    [Fact]
    public async Task Contract_ReadMany_PublishesItsDeclaration()
    {
        await using var host = await StartAsync();

        using var response = await host.Client().GetAsync("/api/openapi.json", Ct);
        using var contract = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        var cache = contract.RootElement.GetProperty("paths").GetProperty(Path).GetProperty("get")
            .GetProperty(OwnedCacheEntry.Extension);
        Assert.Equal("open-loan", cache.GetProperty("resource").GetString());
        Assert.Equal("shelf-service:open-loan:read-many:{user}", cache.GetProperty("key").GetString());
        Assert.Equal("user", cache.GetProperty("keyedBy").GetString());
        Assert.Equal(ShelfHost.ReadScope, cache.GetProperty("scope").GetString());
        Assert.Equal("forbidden", cache.GetProperty("context").GetProperty("organization").GetString());
        Assert.Equal("required", cache.GetProperty("context").GetProperty("user").GetString());
        Assert.Equal("forbidden", cache.GetProperty("context").GetProperty("provider").GetString());
        Assert.Equal("user", cache.GetProperty("owner").GetProperty("axis").GetString());
        Assert.Superset(
            new HashSet<string?> { "borrower", "organization", "title" },
            cache.GetProperty("fields").EnumerateArray().Select(value => value.GetString()).ToHashSet());
        Assert.Empty(cache.GetProperty("perCaller").EnumerateArray());
        Assert.Equal(CacheableReadManyBuilder<LoanEntity, OpenLoan>.DefaultAtMost, cache.GetProperty("readMany").GetProperty("atMost").GetInt32());
        Assert.Equal(["some-service"], cache.GetProperty("assertedBy").EnumerateArray().Select(value => value.GetString()));
    }

    // A page is a slice of the entry, in its order, with the borrower's whole count: the page the database gives.
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(1, 50)]
    public async Task Get_ReadMany_APage_FromTheEntry_IsThePageTheDatabaseGives(int number, int size)
    {
        await using var host = await StartAsync();
        var query = $"{Path}{Fields}&page[number]={number}&page[size]={size}";

        var fromDatabase = await PageBodyAsync(host, query);
        Assert.True(await Redis.KeyExistsAsync(ListKey(_borrower)));
        var fromEntry = await PageBodyAsync(host, query);

        Assert.Equal(fromDatabase, fromEntry);
        using var body = JsonDocument.Parse(fromEntry);
        Assert.Equal(2, body.RootElement.GetProperty("meta").GetProperty("totalCount").GetInt32());
        string[] all = [_first.ToString(), _second.ToString()];
        Assert.Equal(
            all.Skip((number - 1) * size).Take(size),
            body.RootElement.GetProperty("data").EnumerateArray().Select(loan => loan.GetProperty("id").GetString()));
    }

    // A borrower with more open loans than an entry holds is never cached: every page is the database's, whole count
    // and all.
    [Fact]
    public async Task Get_ReadMany_MoreRowsThanAnEntryHolds_NeverCachedAndPagedByTheDatabase()
    {
        await using var host = await StartAsync(list: services => services.AddQuery<IListOpenLoans, FewOpenLoansList>());
        var third = Guid.NewGuid();
        await host.InScopeAsync(_library, async database =>
        {
            database.Loans.Add(new LoanEntity { Id = third, OrganizationId = _library, Borrower = _borrower, Title = "Zodiac", Version = 1 });
            await database.SaveChangesAsync(Ct);
        });
        await Redis.KeyDeleteAsync(ListKey(_borrower));

        using var first = JsonDocument.Parse(await PageBodyAsync(host, $"{Path}{Fields}&page[number]=1&page[size]=2"));
        using var second = JsonDocument.Parse(await PageBodyAsync(host, $"{Path}{Fields}&page[number]=2&page[size]=2"));

        Assert.False(await Redis.KeyExistsAsync(ListKey(_borrower)));
        Assert.Equal(3, first.RootElement.GetProperty("meta").GetProperty("totalCount").GetInt32());
        Assert.Equal(
            [_first.ToString(), _second.ToString(), third.ToString()],
            first.RootElement.GetProperty("data").EnumerateArray().Concat(second.RootElement.GetProperty("data").EnumerateArray())
                .Select(loan => loan.GetProperty("id").GetString()));
    }

    // Exactly as many as an entry holds is cached.
    [Fact]
    public async Task Get_ReadMany_AsManyRowsAsAnEntryHolds_Cached()
    {
        await using var host = await StartAsync(list: services => services.AddQuery<IListOpenLoans, FewOpenLoansList>());

        (await host.GetAsync($"{Path}{Fields}", null, _borrower)).Dispose();

        Assert.True(await Redis.KeyExistsAsync(ListKey(_borrower)));
    }

    [Fact]
    public async Task Start_AReadManyReadWithAScopeItsOperationDoesNotDemand_Refused()
    {
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => ShelfHost.StartAsync(redis.Service, services =>
            services.AddQuery<IListOpenLoans, OtherScopeOpenLoansList>().AddOwnedCache<ShelfContext>()));

        Assert.Contains("demands shelves.read, not the read-many's loans.other", refusal.Message);
    }

    // Each shape a client couldn't repeat refuses the host: something it could answer by, a page it couldn't slice, or
    // a context or scope it doesn't publish.
    [Theory]
    [InlineData("filtered", "it takes 'title' besides the fieldset, the page number, the page size")]
    [InlineData("sorted", "it takes 'sort' besides the fieldset, the page number, the page size")]
    [InlineData("unpaged", "it doesn't take the page number and the page size")]
    [InlineData("small-pages", "its page holds at most 10, fewer than the 200 an entry holds")]
    [InlineData("organization-optional", "it doesn't forbid the organization in context")]
    [InlineData("any-scope", "it accepts any of several scopes")]
    [InlineData("filter", "it has endpoint filters")]
    public async Task Start_AReadManyShapeAClientCannotRepeat_Refused(string shape, string expected)
    {
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => ShelfHost.StartAsync(redis.Service, services =>
            services.AddSingleton(new ReadManyRefusal(shape)).AddQuery<IListOpenLoans, OpenLoansList>().AddOwnedCache<ShelfContext>()));

        Assert.Contains("serves the cached read-many OpenLoansList", refusal.Message);
        Assert.Contains(expected, refusal.Message);
    }

    [Fact]
    public async Task Start_AReadManyOrderedByAColumnRowsMayShare_Refused()
    {
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => ShelfHost.StartAsync(redis.Service, services =>
            services.AddQuery<IListOpenLoans, TitleOrderedOpenLoansList>().AddOwnedCache<ShelfContext>()));

        Assert.Contains("orders its read-many last by LoanEntity.Title, which isn't unique", refusal.Message);
    }

    // Only a change to one of its own rows invalidates an entry, so a read-many reading another table, through a
    // navigation or an order that also narrows, can't be declared.
    [Fact]
    public void Declare_AReadManySelectingThroughANavigation_Refused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => ShelfQueries.Declare<PlacedShelvesList, PlacedShelvesList>());

        Assert.Contains("PlacedShelvesList selects its rows (Where) by ShelfEntity.Placements, which is no column of its own", refusal.Message);
    }

    [Fact]
    public void Declare_AReadManyWhoseOrderNarrows_Refused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => ShelfQueries.Declare<IListOpenLoans, NarrowingOrderOpenLoansList>());

        Assert.Contains("NarrowingOrderOpenLoansList's read-many order (OrderBy) calls Where", refusal.Message);
    }

    [Fact]
    public void Declare_AReadManyWithoutItsOrder_Refused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => ShelfQueries.Declare<IListOpenLoans, UnorderedOpenLoansList>());

        Assert.Contains("without its order (OrderBy)", refusal.Message);
    }

    // A read-many's resource holds only what its rows hold: one another read gives, with what it works out per caller,
    // is refused.
    [Fact]
    public void Declare_AReadManyOfAResourceAnotherReadGives_Refused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => new CacheableQueries(new ServiceCollection()
            .AddQuery<IGetLoanQuery, GetLoanQuery>()
            .AddQuery<BorrowersLoansAsLoans, BorrowersLoansAsLoans>()));

        Assert.Contains("BorrowersLoansAsLoans is a cached read-many of loan, which GetLoanQuery also reads", refusal.Message);
    }

    [Fact]
    public void AtMost_LessThanOne_Refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CacheableReadManyBuilder<LoanEntity, OpenLoan>().AtMost(0));

    private async Task<ShelfHost> StartAsync(
        IReadOnlyDictionary<string, string?>? settings = null,
        BetweenReadAndFill? between = null,
        Action<IServiceCollection>? list = null)
    {
        var host = await ShelfHost.StartAsync(
            redis.Service,
            services =>
            {
                (list ?? (each => each.AddQuery<IListOpenLoans, OpenLoansList>()))(services);
                services.AddOwnedCache<ShelfContext>();

                if (between is not null)
                {
                    services.ConfigureDbContext<ShelfContext>(options => options.AddInterceptors(between));
                }
            },
            settings: settings);

        await SeedAsync(host, cache: true);
        return host;
    }

    /// <summary>
    /// Two open loans of the borrower in two organizations, a returned one, and another borrower's; the tombstones their
    /// arrival left cleared.
    /// </summary>
    private async Task SeedAsync(ShelfHost host, bool cache)
    {
        foreach (var organization in new[] { _library, _branch })
        {
            await host.InScopeAsync(organization, async database =>
            {
                database.Loans.AddRange(new[]
                {
                    new LoanEntity { Id = _second, OrganizationId = _library, Borrower = _borrower, Title = "Cryptonomicon", Version = 1 },
                    new LoanEntity { Id = _first, OrganizationId = _branch, Borrower = _borrower, Title = "Anathem", Version = 1 },
                    new LoanEntity { Id = _returned, OrganizationId = _library, Borrower = _borrower, Title = "Dune", Returned = true, Version = 1 },
                    new LoanEntity { Id = _emma, OrganizationId = _library, Borrower = _other, Title = "Emma", Version = 1 },
                }.Where(loan => loan.OrganizationId == organization));
                await database.SaveChangesAsync(Ct);
            });
        }

        if (cache)
        {
            await Redis.KeyDeleteAsync([ListKey(_borrower), ListKey(_other)]);
        }
    }

    private async Task WaitGoneAsync(string key)
    {
        var until = DateTime.UtcNow.AddSeconds(15);

        while (await Redis.KeyExistsAsync(key))
        {
            Assert.True(DateTime.UtcNow < until, $"{key} outlived its tombstone.");
            await Task.Delay(100, Ct);
        }
    }

    private static async Task<string> BodyAsync(ShelfHost host, Guid borrower)
    {
        using var response = await host.GetAsync($"{Path}{Fields}", null, borrower);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<string> PageBodyAsync(ShelfHost host, string query)
    {
        using var response = await host.GetAsync(query, null, _borrower);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private static async Task<string[]> IdsAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
            return [.. body.RootElement.GetProperty("data").EnumerateArray().Select(loan => loan.GetProperty("id").GetString()!)];
        }
    }

    private async Task<Dictionary<string, string>> Entry(string key) =>
        (await Redis.HashGetAllAsync(key)).ToDictionary(field => field.Name.ToString(), field => field.Value.ToString());

    /// <summary>
    /// Commits a change once, after the read-many's rows are read and before they are filled.
    /// </summary>
    private sealed class BetweenReadAndFill : DbCommandInterceptor
    {
        public Func<Task>? Then { get; set; }

        public override async ValueTask<InterceptionResult> DataReaderClosingAsync(
            DbCommand command,
            DataReaderClosingEventData eventData,
            InterceptionResult result)
        {
            if (command.CommandText.Contains("\"Borrower\" =", StringComparison.Ordinal) && Then is { } then)
            {
                Then = null;
                await then();
            }

            return result;
        }
    }
}
