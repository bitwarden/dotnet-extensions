using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Resources.Caching.Tests;

/// <summary>
/// One table backing two cacheable queries: a loan by identifier, and the person in context's loan in the organization
/// in context, keyed by its borrower within the organization, which several loans may hold.
/// </summary>
public sealed partial class MultiKeyTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private readonly Guid _organization = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();
    private readonly Guid _borrower = Guid.NewGuid();
    private readonly Guid _loan = Guid.NewGuid();
    private ConnectionMultiplexer _redis = null!;

    private IDatabase Redis => _redis.GetDatabase();

    private string ById => $"{ShelfHost.ServiceName}:loan:{_loan}";

    // A commit allowance of two seconds, which the startup check accepts with a command timeout of one second and a
    // fill limit of half of one.
    private static readonly TimeSpan Allowance = TimeSpan.FromSeconds(2);

    private static readonly Dictionary<string, string?> ShortAllowance = new()
    {
        [ShelfHost.CommandTimeoutKey] = "1",
        ["Caching:Owned:FillLimit"] = "00:00:00.5",
        ["Caching:Owned:CommitAllowance"] = "00:00:02",
    };

    private string Owner => $$"""{"organization":"{{_organization}}"}""";

    private string ByBorrower(Guid borrower, Guid? organization = null) =>
        $"{ShelfHost.ServiceName}:loan:by-borrower:{organization ?? _organization}:{borrower}";

    public async ValueTask InitializeAsync() =>
        _redis = await ConnectionMultiplexer.ConnectAsync(redis.Hostname);

    public async ValueTask DisposeAsync() => await _redis.DisposeAsync();

    [Fact]
    public async Task Get_Current_FillsTheEntryUnderTheOrganizationAndTheBorrower()
    {
        await using var host = await StartAsync();

        using var response = await host.GetAsync("/api/v1/loans/current", _organization, _borrower);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = await Entry(ByBorrower(_borrower));
        Assert.Equal("1", entry[OwnedCacheEntry.Version]);
        Assert.Equal($$"""{"organization":"{{_organization}}"}""", entry[OwnedCacheEntry.Owner]);
        Assert.Contains(_loan.ToString(), entry[OwnedCacheEntry.Data]);
    }

    // The owner is part of the key: the same borrower in another organization is another entry, and not this one's.
    [Fact]
    public async Task Get_Current_SameBorrowerInAnotherOrganization_KeptApart()
    {
        await using var host = await StartAsync();

        (await host.GetAsync("/api/v1/loans/current", _organization, _borrower)).Dispose();
        using var other = await host.GetAsync("/api/v1/loans/current", _other, _borrower);

        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal("1", (await Entry(ByBorrower(_borrower, _other)))[OwnedCacheEntry.None]);
        Assert.False((await Entry(ByBorrower(_borrower))).ContainsKey(OwnedCacheEntry.None));
    }

    [Fact]
    public async Task Get_Current_FromTheEntry_IsTheSameResponseAsFromTheDatabase()
    {
        await using var host = await StartAsync();

        var fromDatabase = await BodyAsync(host);
        Assert.True(await Redis.KeyExistsAsync(ByBorrower(_borrower)));
        var fromEntry = await BodyAsync(host);

        Assert.Equal(fromDatabase, fromEntry);
    }

    // A change to the row tombstones its entry under every key the table's queries keep it by.
    [Fact]
    public async Task Change_Row_InvalidatesBothKeys()
    {
        await using var host = await StartAsync();
        (await host.GetAsync($"/api/v1/loans/{_loan}", _organization)).Dispose();
        (await host.GetAsync("/api/v1/loans/current", _organization, _borrower)).Dispose();

        await host.InScopeAsync(_organization, async database =>
        {
            (await database.Loans.SingleAsync(row => row.Id == _loan, Ct)).Returned = true;
            await database.SaveChangesAsync(Ct);
        });

        foreach (var key in new[] { ById, ByBorrower(_borrower) })
        {
            var entry = await Entry(key);
            Assert.Equal("1", entry[OwnedCacheEntry.Gone]);
            Assert.Equal("1", entry[OwnedCacheEntry.Version]);
        }

        // The version the change committed is filled again under both.
        (await host.GetAsync("/api/v1/loans/current", _organization, _borrower)).Dispose();
        Assert.Equal("2", (await Entry(ByBorrower(_borrower)))[OwnedCacheEntry.Version]);
        Assert.Contains("\"returned\":true", (await Entry(ByBorrower(_borrower)))[OwnedCacheEntry.Data]);
    }

    // A loan passed to another borrower: the key it left has its version dead; the key it arrived at, which another
    // loan could hold, every version.
    [Fact]
    public async Task Change_KeyMoved_InvalidatesTheOldKeyAndTheNewAtEveryVersion()
    {
        await using var host = await StartAsync();
        var next = Guid.NewGuid();
        (await host.GetAsync("/api/v1/loans/current", _organization, _borrower)).Dispose();
        (await host.GetAsync("/api/v1/loans/current", _organization, next)).Dispose();
        Assert.Equal("1", (await Entry(ByBorrower(next)))[OwnedCacheEntry.None]);

        await host.InScopeAsync(_organization, async database =>
        {
            (await database.Loans.SingleAsync(row => row.Id == _loan, Ct)).Borrower = next;
            await database.SaveChangesAsync(Ct);
        });

        Assert.Equal("1", (await Entry(ByBorrower(_borrower)))[OwnedCacheEntry.Version]);
        Assert.Equal("1", (await Entry(ByBorrower(_borrower)))[OwnedCacheEntry.Gone]);
        Assert.Equal(Invalidation.EveryVersion.ToString(), (await Entry(ByBorrower(next)))[OwnedCacheEntry.Version]);
        Assert.Equal("1", (await Entry(ByBorrower(next)))[OwnedCacheEntry.Gone]);
        Assert.Equal("1", (await Entry(ById))[OwnedCacheEntry.Gone]);

        // Answered by the database, filled under neither.
        using var moved = await host.GetAsync("/api/v1/loans/current", _organization, next);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal("1", (await Entry(ByBorrower(next)))[OwnedCacheEntry.Gone]);
        Assert.Equal(HttpStatusCode.NotFound, (await host.GetAsync("/api/v1/loans/current", _organization, _borrower)).StatusCode);
    }

    // A second row under a key whose one row is cached: the entry is dead at every version, the first of the two is
    // answered, and nothing is filled while two hold the key.
    [Fact]
    public async Task Add_SecondRowUnderACachedKey_InvalidatedAndNotRefilledWhileTwo()
    {
        await using var host = await StartAsync();
        (await host.GetAsync("/api/v1/loans/current", _organization, _borrower)).Dispose();
        Assert.Equal("1", (await Entry(ByBorrower(_borrower)))[OwnedCacheEntry.Version]);
        var second = Guid.NewGuid();

        await host.InScopeAsync(_organization, async database =>
        {
            // Titled to come first: the read answers it, not the one the entry held.
            database.Loans.Add(new LoanEntity { Id = second, OrganizationId = _organization, Borrower = _borrower, Title = "A", Version = 1 });
            await database.SaveChangesAsync(Ct);
        });

        Assert.Equal("1", (await Entry(ByBorrower(_borrower)))[OwnedCacheEntry.Gone]);
        Assert.Equal(Invalidation.EveryVersion.ToString(), (await Entry(ByBorrower(_borrower)))[OwnedCacheEntry.Version]);

        for (var read = 0; read < 2; read++)
        {
            var body = await BodyAsync(host);
            Assert.Contains(second.ToString(), body);
            Assert.Equal("1", (await Entry(ByBorrower(_borrower)))[OwnedCacheEntry.Gone]);
        }
    }

    // Two rows under a key with nothing cached: the first is answered and nothing is filled.
    [Fact]
    public async Task Get_Current_TwoRows_AnswersTheFirstAndFillsNothing()
    {
        await using var host = await StartAsync();
        var second = Guid.NewGuid();
        await host.InScopeAsync(_organization, async database =>
        {
            database.Loans.Add(new LoanEntity { Id = second, OrganizationId = _organization, Borrower = _borrower, Title = "A", Version = 1 });
            await database.SaveChangesAsync(Ct);
        });
        await Redis.KeyDeleteAsync(ByBorrower(_borrower));

        var body = await BodyAsync(host);

        Assert.Contains(second.ToString(), body);
        Assert.False(await Redis.KeyExistsAsync(ByBorrower(_borrower)));
    }

    // "Nothing recorded" is cached with its owner, and a row arriving makes it dead at every version.
    [Fact]
    public async Task Get_Current_NothingRecorded_CachedUntilARowArrives()
    {
        await using var host = await StartAsync();
        var nobody = Guid.NewGuid();

        using var none = await host.GetAsync("/api/v1/loans/current", _organization, nobody);

        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        var entry = await Entry(ByBorrower(nobody));
        Assert.Equal("-1", entry[OwnedCacheEntry.Version]);
        Assert.Equal($$"""{"organization":"{{_organization}}"}""", entry[OwnedCacheEntry.Owner]);

        await host.InScopeAsync(_organization, async database =>
        {
            database.Loans.Add(new LoanEntity { Id = Guid.NewGuid(), OrganizationId = _organization, Borrower = nobody, Version = 1 });
            await database.SaveChangesAsync(Ct);
        });

        Assert.Equal(Invalidation.EveryVersion.ToString(), (await Entry(ByBorrower(nobody)))[OwnedCacheEntry.Version]);
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/v1/loans/current", _organization, nobody)).StatusCode);
    }

    // A row arriving where "nothing recorded" was cached: its tombstone lives the commit allowance, refusing within it a
    // reader that read before the commit, and the key is then filled with the row that arrived.
    [Fact]
    public async Task Add_Row_TombstoneLivesTheCommitAllowanceThenTheKeyRefillsWithTheRow()
    {
        await using var host = await StartAsync(ShortAllowance);
        var store = host.Services.GetRequiredService<IOwnedCacheStore>();
        var nobody = Guid.NewGuid();
        var arrived = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await host.GetAsync("/api/v1/loans/current", _organization, nobody)).StatusCode);

        await host.InScopeAsync(_organization, async database =>
        {
            database.Loans.Add(new LoanEntity { Id = arrived, OrganizationId = _organization, Borrower = nobody, Title = "C", Version = 1 });
            await database.SaveChangesAsync(Ct);
        });

        Assert.Equal(Invalidation.EveryVersion.ToString(), (await Entry(ByBorrower(nobody)))[OwnedCacheEntry.Version]);
        Assert.InRange((await Redis.KeyTimeToLiveAsync(ByBorrower(nobody)))!.Value, TimeSpan.Zero, Allowance);

        // A reader that found nothing before the arrival committed.
        Assert.False(await store.FillAsync(ByBorrower(nobody), CacheFill.NothingRecorded, Owner, null, TimeSpan.FromMinutes(1)));

        await ExpiredAsync(ByBorrower(nobody));
        using var response = await host.GetAsync("/api/v1/loans/current", _organization, nobody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = await Entry(ByBorrower(nobody));
        Assert.False(entry.ContainsKey(OwnedCacheEntry.Gone));
        Assert.Equal("1", entry[OwnedCacheEntry.Version]);
        Assert.Contains("\"title\":\"C\"", entry[OwnedCacheEntry.Data]);
    }

    // A second row arriving under a key whose first is cached: within the allowance the reader that read the first
    // before the commit is refused, and after the tombstone expires the two rows are answered and nothing is cached.
    [Fact]
    public async Task Add_SecondRowUnderACachedKey_NothingCachedAfterTheArrivalsTombstoneExpires()
    {
        await using var host = await StartAsync(ShortAllowance);
        var store = host.Services.GetRequiredService<IOwnedCacheStore>();
        (await host.GetAsync("/api/v1/loans/current", _organization, _borrower)).Dispose();
        var first = (await Entry(ByBorrower(_borrower)))[OwnedCacheEntry.Data];
        var second = Guid.NewGuid();

        await host.InScopeAsync(_organization, async database =>
        {
            database.Loans.Add(new LoanEntity { Id = second, OrganizationId = _organization, Borrower = _borrower, Title = "A", Version = 1 });
            await database.SaveChangesAsync(Ct);
        });

        Assert.InRange((await Redis.KeyTimeToLiveAsync(ByBorrower(_borrower)))!.Value, TimeSpan.Zero, Allowance);

        // A reader that read the one row before the arrival committed.
        Assert.False(await store.FillAsync(ByBorrower(_borrower), 1, Owner, first, TimeSpan.FromMinutes(1)));

        await ExpiredAsync(ByBorrower(_borrower));

        for (var read = 0; read < 2; read++)
        {
            Assert.Contains(second.ToString(), await BodyAsync(host));
            Assert.False(await Redis.KeyExistsAsync(ByBorrower(_borrower)));
        }
    }

    // A change to a row that was already there is no arrival: its tombstone keeps the full lifetime.
    [Fact]
    public async Task Change_Row_TombstoneKeepsTheFullLifetime()
    {
        await using var host = await StartAsync(ShortAllowance);
        (await host.GetAsync("/api/v1/loans/current", _organization, _borrower)).Dispose();

        await host.InScopeAsync(_organization, async database =>
        {
            (await database.Loans.SingleAsync(row => row.Id == _loan, Ct)).Returned = true;
            await database.SaveChangesAsync(Ct);
        });

        Assert.Equal("1", (await Entry(ByBorrower(_borrower)))[OwnedCacheEntry.Gone]);
        Assert.True((await Redis.KeyTimeToLiveAsync(ByBorrower(_borrower)))!.Value > TimeSpan.FromMinutes(1));
    }

    // A row with no key yet (a loan nobody has borrowed) has no entry under the key, and its deletion touches none.
    [Fact]
    public async Task Change_RowWithoutAKey_TouchesNoKeyedEntry()
    {
        await using var host = await StartAsync();
        var unborrowed = Guid.NewGuid();

        await host.InScopeAsync(_organization, async database =>
        {
            database.Loans.Add(new LoanEntity { Id = unborrowed, OrganizationId = _organization, Version = 1 });
            await database.SaveChangesAsync(Ct);
            database.Loans.Remove(await database.Loans.SingleAsync(row => row.Id == unborrowed, Ct));
            await database.SaveChangesAsync(Ct);
        });

        Assert.Empty(await KeysAsync($"{ShelfHost.ServiceName}:loan:by-borrower:{_organization}:*"));
    }

    // The service's own read about an owner its request doesn't name: narrowed by the owner named, filled, and then
    // answered from the entry it filled.
    [Fact]
    public async Task ReadOwned_NoOwnerInContext_ReadsWithinTheOwnerNamedAndFills()
    {
        await using var host = await StartAsync();
        using var scope = host.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IGetBorrowersLoanQuery>();

        var found = await query.ReadOwnedAsync(_organization, _borrower, cancellationToken: Ct);
        var elsewhere = await query.ReadOwnedAsync(_other, _borrower, cancellationToken: Ct);

        Assert.Equal(_loan.ToString(), found!.Data.Id);
        Assert.Null(elsewhere);
        Assert.Equal("1", (await Entry(ByBorrower(_borrower)))[OwnedCacheEntry.Version]);
        await Redis.HashSetAsync(ByBorrower(_borrower), OwnedCacheEntry.Data, (await Entry(ByBorrower(_borrower)))[OwnedCacheEntry.Data].Replace("\"B\"", "\"From the entry\"", StringComparison.Ordinal));
        Assert.Equal("From the entry", (await query.ReadOwnedAsync(_organization, _borrower, cancellationToken: Ct))!.Data.Title);
    }

    [Fact]
    public async Task Get_Current_WithoutAPerson_Refused()
    {
        await using var host = await StartAsync();

        using var response = await host.GetAsync("/api/v1/loans/current", _organization);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Contract_ReadOfCurrent_PublishesTheContextsPlaceholders()
    {
        await using var host = await StartAsync();

        using var response = await host.Client().GetAsync("/api/openapi.json", Ct);
        using var contract = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        var paths = contract.RootElement.GetProperty("paths");
        var current = paths.GetProperty("/api/v1/loans/current").GetProperty("get").GetProperty(OwnedCacheEntry.Extension);
        Assert.Equal("shelf-service:loan:by-borrower:{organization}:{user}", current.GetProperty("key").GetString());
        Assert.Equal("required", current.GetProperty("context").GetProperty("user").GetString());
        var byId = paths.GetProperty("/api/v1/loans/{id}").GetProperty("get").GetProperty(OwnedCacheEntry.Extension);
        Assert.Equal("shelf-service:loan:{key}", byId.GetProperty("key").GetString());

        // Who may assert a person's context there: the services the operation names, and none where it names none.
        Assert.Equal(["some-service"], current.GetProperty("assertedBy").EnumerateArray().Select(caller => caller.GetString()));
        Assert.Empty(byId.GetProperty("assertedBy").EnumerateArray());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<ShelfHost> StartAsync(IReadOnlyDictionary<string, string?>? settings = null)
    {
        var host = await ShelfHost.StartAsync(
            redis.Service,
            services => services
                .AddQuery<IGetLoanQuery, GetLoanQuery>()
                .AddQuery<IGetBorrowersLoanQuery, GetBorrowersLoanQuery>()
                .AddOwnedCache<ShelfContext>(),
            settings: settings);

        await host.InScopeAsync(_organization, async database =>
        {
            database.Loans.Add(new LoanEntity { Id = _loan, OrganizationId = _organization, Borrower = _borrower, Title = "B", Version = 1 });
            await database.SaveChangesAsync(Ct);
        });

        // The loan's arrival marked its key dead at every version, as a row arriving under a key several may hold does;
        // these tests begin after that tombstone has gone.
        await Redis.KeyDeleteAsync(ByBorrower(_borrower));

        return host;
    }

    // Waits, a little past the allowance at most, for an arrival's tombstone to expire.
    private async Task ExpiredAsync(string key)
    {
        var until = DateTime.UtcNow + Allowance + TimeSpan.FromSeconds(3);
        while (await Redis.KeyExistsAsync(key))
        {
            Assert.True(DateTime.UtcNow < until, $"{key} outlived the commit allowance.");
            await Task.Delay(100, Ct);
        }
    }

    private async Task<string> BodyAsync(ShelfHost host)
    {
        using var response = await host.GetAsync("/api/v1/loans/current?fields[loan]=title,returned,version", _organization, _borrower);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<Dictionary<string, string>> Entry(string key) =>
        (await Redis.HashGetAllAsync(key)).ToDictionary(field => field.Name.ToString(), field => field.Value.ToString());

    private async Task<List<string>> KeysAsync(string pattern)
    {
        var server = _redis.GetServers()[0];
        var keys = new List<string>();
        await foreach (var key in server.KeysAsync(pattern: pattern))
        {
            keys.Add(key.ToString());
        }

        return keys;
    }
}
