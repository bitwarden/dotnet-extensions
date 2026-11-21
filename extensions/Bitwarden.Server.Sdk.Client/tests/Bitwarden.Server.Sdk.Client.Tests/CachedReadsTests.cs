using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Resources.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// A client answers a read from its service's owned cache only where the service would have given
/// the same answer to the same caller.
/// </summary>
public sealed class CachedReadsTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private const string Service = "shelf-service";

    private const string Contract = """
        {
          "paths": {
            "/api/v1/books/{id}": { "get": { "operationId": "getBook", "x-bitwarden-cache": {
              "resource": "book", "key": "shelf-service:book:{key}", "keyedBy": "id", "scope": "shelves.read",
              "context": { "organization": "required", "user": "optional", "provider": "optional" },
              "owner": { "axis": "organization", "self": false },
              "fields": [ "title", "version" ], "perCaller": [ "shelfmark" ], "format": 1 } } },
            "/api/v1/books/{id}/old": { "get": { "operationId": "getBookOld", "x-bitwarden-cache": {
              "resource": "book", "key": "shelf-service:book:{key}", "keyedBy": "id", "scope": "shelves.read",
              "context": { "organization": "required", "user": "optional", "provider": "optional" },
              "owner": { "axis": "organization", "self": false },
              "fields": [ "title", "version" ] } } },
            "/api/v1/libraries/{id}": { "get": { "operationId": "getLibrary", "x-bitwarden-cache": {
              "resource": "library", "key": "shelf-service:library:{key}", "keyedBy": "id", "scope": "shelves.read",
              "context": { "organization": "optional", "user": "optional", "provider": "optional" },
              "owner": { "axis": "organization", "self": true },
              "fields": [ "name" ], "format": 1 } } },
            "/api/v1/quotas": { "get": { "operationId": "listQuotas", "x-bitwarden-cache": {
              "resource": "quota", "key": "shelf-service:quota:{key}", "keyedBy": "organization", "scope": "shelves.read",
              "context": { "organization": "optional", "user": "optional", "provider": "optional" },
              "owner": { "axis": "organization", "self": false },
              "fields": [ "limit" ], "format": 1 } } },
            "/api/v1/permits": { "get": { "operationId": "listPermits", "x-bitwarden-cache": {
              "resource": "permit", "key": "shelf-service:permit:{key}", "keyedBy": "organization", "scope": "shelves.read",
              "context": { "organization": "optional", "user": "optional", "provider": "optional" },
              "owner": { "axis": "organization", "self": false, "acrossOwners": true },
              "fields": [ "limit" ], "format": 1 } } },
            "/api/v1/loans/{id}": { "get": { "operationId": "getLoan", "x-bitwarden-cache": {
              "resource": "loan", "key": "shelf-service:loan:{key}", "keyedBy": "id", "scope": "shelves.read",
              "context": { "organization": "required", "user": "optional", "provider": "optional" },
              "owner": { "axis": "organization", "self": false },
              "fields": [ "organization", "borrower", "returned" ], "format": 1 } } },
            "/api/v1/open-loans": { "get": { "operationId": "listOpenLoans", "x-bitwarden-cache": {
              "resource": "open-loan", "key": "shelf-service:open-loan:read-many:{user}", "keyedBy": "user",
              "scope": "shelves.read", "assertedBy": [ "lending-desk" ],
              "context": { "organization": "forbidden", "user": "required", "provider": "forbidden" },
              "owner": { "axis": "user", "self": false },
              "fields": [ "title", "organization" ], "perCaller": [ ], "format": 1,
              "readMany": { "atMost": 200 } } } },
            "/api/v1/labels/{id}": { "get": { "operationId": "getLabel" } }
          }
        }
        """;

    private static readonly Guid Ours = Guid.NewGuid();
    private static readonly Guid Theirs = Guid.NewGuid();

    private readonly Guid _book = Guid.NewGuid();
    private readonly MutableContext _context = new() { OrganizationId = Ours };
    private ConnectionMultiplexer _redis = null!;
    private CachedReadsConnection _connection = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _redis = await ConnectionMultiplexer.ConnectAsync(redis.Hostname);

        // On a clock that stands still: a read waits only CachedReads.Patience for Redis, which a
        // first connection or a reply can take on a busy machine, and the read then misses by
        // design. On this clock the patience never runs out, so the tests see the cache's answer.
        _connection = new CachedReadsConnection(redis.Hostname, new FakeTimeProvider());
        await Store($"{Service}:book:{_book}", ("v", "3"), ("o", $$"""{"organization":"{{Ours}}"}"""),
            ("d", $$$"""{"id":"{{{_book}}}","type":"book","attributes":{"title":"Dune","version":"3"}}"""));
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task ReadAsync_TheCallsOwnOrganization_HitShapedToTheFieldsAskedFor()
    {
        var answer = await Reads().ReadAsync("getBook", _book, ["title"], Token);

        Assert.Equal(CachedAnswerKind.Hit, answer.Kind);
        using var resource = JsonDocument.Parse(answer.Resource!);
        Assert.Equal(_book.ToString(), resource.RootElement.GetProperty("id").GetString());
        Assert.Equal("Dune", resource.RootElement.GetProperty("attributes").GetProperty("title").GetString());
        Assert.False(resource.RootElement.GetProperty("attributes").TryGetProperty("version", out _));
    }

    // A repeated read inside a request memo waits on Redis no more than the call it stands for would wait on the service.
    [Fact]
    public async Task ReadAsync_TwiceInAMemo_ReadsRedisOnce_UntilForgottenOrFresh()
    {
        var reads = Reads();
        using var memo = RequestMemo.Begin();

        var first = await reads.ReadAsync("getBook", _book, ["title"], Token);
        await Store($"{Service}:book:{_book}", ("v", "4"), ("o", $$"""{"organization":"{{Ours}}"}"""),
            ("d", $$$"""{"id":"{{{_book}}}","type":"book","attributes":{"title":"Children of Dune","version":"4"}}"""));
        var remembered = await reads.ReadAsync("getBook", _book, ["title"], Token);

        CachedAnswer fresh;
        using (OutgoingCall.Fresh())
        {
            fresh = await reads.ReadAsync("getBook", _book, ["title"], Token);
        }

        Assert.Contains("\"Dune\"", first.Resource, StringComparison.Ordinal);
        Assert.Contains("\"Dune\"", remembered.Resource, StringComparison.Ordinal);
        Assert.Contains("Children of Dune", fresh.Resource, StringComparison.Ordinal);
    }

    // The entry is remembered; whether it answers is decided for each call, as without the memo.
    [Fact]
    public async Task ReadAsync_InAMemo_AnotherOrganizationStillMisses()
    {
        var reads = Reads();
        using var memo = RequestMemo.Begin();

        Assert.Equal(CachedAnswerKind.Hit, (await reads.ReadAsync("getBook", _book, ["title"], Token)).Kind);
        _context.OrganizationId = Theirs;

        Assert.Equal(CachedAnswerKind.Miss, (await reads.ReadAsync("getBook", _book, ["title"], Token)).Kind);
    }

    [Fact]
    public async Task ReadAsync_InAMemoSwitchedOff_ReadsRedisEachTime()
    {
        var reads = Reads(configuration: new Dictionary<string, string?> { [RequestMemo.EnabledKey] = "false" });
        using var memo = RequestMemo.Begin();

        await reads.ReadAsync("getBook", _book, ["title"], Token);
        await Store($"{Service}:book:{_book}", ("v", "4"), ("o", $$"""{"organization":"{{Ours}}"}"""),
            ("d", $$$"""{"id":"{{{_book}}}","type":"book","attributes":{"title":"Children of Dune","version":"4"}}"""));

        Assert.Contains("Children of Dune", (await reads.ReadAsync("getBook", _book, ["title"], Token)).Resource, StringComparison.Ordinal);
    }

    // No fieldset asks for every attribute: a hit, whole, where nothing is worked out per caller.
    [Fact]
    public async Task ReadAsync_NoFieldsetNothingPerCaller_HitWhole()
    {
        var loan = Guid.NewGuid();
        await Store($"{Service}:loan:{loan}", ("v", "1"), ("o", $$"""{"organization":"{{Ours}}"}"""),
            ("d", $$$"""{"id":"{{{loan}}}","type":"loan","attributes":{"organization":"{{{Ours}}}","borrower":null,"returned":false}}"""));

        var answer = await Reads().ReadAsync("getLoan", loan, [], Token);

        Assert.Equal(CachedAnswerKind.Hit, answer.Kind);
        using var resource = JsonDocument.Parse(answer.Resource!);
        Assert.Equal(3, resource.RootElement.GetProperty("attributes").EnumerateObject().Count());
    }

    // Every attribute includes those worked out per caller, which the cache never holds.
    [Fact]
    public async Task ReadAsync_NoFieldsetSomethingPerCaller_Miss()
    {
        Assert.Equal(CachedAnswerKind.Miss, (await Reads().ReadAsync("getBook", _book, [], Token)).Kind);
    }

    // An entry or a contract in a format this client doesn't know is a miss.
    [Theory]
    [InlineData("getBook", "2")]
    [InlineData("getBook", null)]
    [InlineData("getBookOld", "1")]
    public async Task ReadAsync_UnknownFormat_Miss(string operation, string? format)
    {
        if (format is null)
        {
            await _redis.GetDatabase().HashDeleteAsync($"{Service}:book:{_book}", OwnedCacheEntry.Format);
        }
        else
        {
            await _redis.GetDatabase().HashSetAsync($"{Service}:book:{_book}", OwnedCacheEntry.Format, format);
        }

        Assert.Equal(CachedAnswerKind.Miss, (await Reads().ReadAsync(operation, _book, ["title"], Token)).Kind);
    }

    [Fact]
    public async Task ReadAsync_AnotherOrganization_Miss()
    {
        _context.OrganizationId = Theirs;

        Assert.Equal(CachedAnswerKind.Miss, (await Reads().ReadAsync("getBook", _book, ["title"], Token)).Kind);
    }

    [Fact]
    public async Task ReadAsync_NoOrganizationWhereTheOperationRequiresOne_Miss()
    {
        _context.OrganizationId = null;

        Assert.Equal(CachedAnswerKind.Miss, (await Reads().ReadAsync("getBook", _book, ["title"], Token)).Kind);
    }

    [Fact]
    public async Task ReadAsync_AFieldTheCacheDoesNotHold_Miss()
    {
        Assert.Equal(CachedAnswerKind.Miss, (await Reads().ReadAsync("getBook", _book, ["title", "shelfmark"], Token)).Kind);
    }

    [Fact]
    public async Task ReadAsync_ScopeTheCredentialDoesNotHold_Miss()
    {
        Assert.Equal(CachedAnswerKind.Miss, (await Reads(scopes: ["shelves.write"]).ReadAsync("getBook", _book, ["title"], Token)).Kind);
    }

    // Read across owners, the service answers any holder of the scope whatever organization the
    // call names, and so does its cache.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAsync_AcrossOwners_HitWhateverOrganizationTheCallNames(bool another)
    {
        var permitted = await APermit();
        _context.OrganizationId = another ? Theirs : null;

        Assert.Equal(CachedAnswerKind.Hit, (await Reads().ReadAsync("listPermits", permitted, ["limit"], Token)).Kind);
    }

    [Fact]
    public async Task ReadAsync_AcrossOwnersWithoutTheScope_Miss()
    {
        var permitted = await APermit();
        _context.OrganizationId = null;

        Assert.Equal(CachedAnswerKind.Miss, (await Reads(scopes: ["shelves.write"]).ReadAsync("listPermits", permitted, ["limit"], Token)).Kind);
    }

    [Fact]
    public async Task ReadAsync_AcrossOwnersEntryWithoutAnOwner_Miss()
    {
        var key = Guid.NewGuid();
        await Store($"{Service}:permit:{key}", ("v", "1"), ("d", $$$"""{"id":"{{{key}}}","type":"permit","attributes":{"limit":3}}"""));
        _context.OrganizationId = null;

        Assert.Equal(CachedAnswerKind.Miss, (await Reads().ReadAsync("listPermits", key, ["limit"], Token)).Kind);
    }

    // An owner field the client can't read is a miss, never a failed call: the cache is the owner's data.
    [Theory]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("""{"organization":7}""")]
    [InlineData("""{"organization":"00000000-0000-0000-0000-000000000001","organization":"00000000-0000-0000-0000-000000000001"}""")]
    public async Task ReadAsync_AnUnreadableOwner_Miss(string owner)
    {
        await Store($"{Service}:quota:{Ours}", ("v", "1"), ("o", owner),
            ("d", $$$"""{"id":"{{{Ours}}}","type":"quota","attributes":{"limit":3}}"""));
        _context.OrganizationId = Ours;

        Assert.Equal(CachedAnswerKind.Miss, (await Reads().ReadAsync("listQuotas", Ours, ["limit"], Token)).Kind);
    }

    // Data the client can't shape is a miss too: not JSON, not an object, or an object naming an attribute twice.
    [Theory]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("""{"id":"x","type":"quota","attributes":{"limit":3,"limit":4}}""")]
    public async Task ReadAsync_UnreadableData_Miss(string data)
    {
        await Store($"{Service}:quota:{Ours}", ("v", "1"), ("o", $$"""{"organization":"{{Ours}}"}"""), ("d", data));
        _context.OrganizationId = Ours;

        Assert.Equal(CachedAnswerKind.Miss, (await Reads().ReadAsync("listQuotas", Ours, ["limit"], Token)).Kind);
    }

    [Fact]
    public async Task ReadAsync_AcrossOwnersTombstone_Miss()
    {
        var key = Guid.NewGuid();
        await Store($"{Service}:permit:{key}", ("v", "2"), ("gone", "1"));

        Assert.Equal(CachedAnswerKind.Miss, (await Reads().ReadAsync("listPermits", key, ["limit"], Token)).Kind);
    }

    // A contract that doesn't say a resource is read across owners keeps the owner check.
    [Fact]
    public async Task ReadAsync_OwnedResourceAnotherOrganization_Miss()
    {
        await Store($"{Service}:quota:{Ours}", ("v", "1"), ("o", $$"""{"organization":"{{Ours}}"}"""),
            ("d", $$$"""{"id":"{{{Ours}}}","type":"quota","attributes":{"limit":3}}"""));
        _context.OrganizationId = Theirs;

        Assert.Equal(CachedAnswerKind.Miss, (await Reads().ReadAsync("listQuotas", Ours, ["limit"], Token)).Kind);
    }

    private async Task<Guid> APermit()
    {
        var organization = Guid.NewGuid();
        await Store($"{Service}:permit:{organization}", ("v", "1"), ("o", $$"""{"organization":"{{organization}}"}"""),
            ("d", $$$"""{"id":"{{{organization}}}","type":"permit","attributes":{"limit":3}}"""));

        return organization;
    }

    [Fact]
    public async Task ReadAsync_Tombstone_Miss()
    {
        var key = Guid.NewGuid();
        await Store($"{Service}:book:{key}", ("v", "4"), ("gone", "1"));

        Assert.Equal(CachedAnswerKind.Miss, (await Reads().ReadAsync("getBook", key, ["title"], Token)).Kind);
    }

    [Fact]
    public async Task ReadAsync_NotCached_Miss()
    {
        Assert.Equal(CachedAnswerKind.Miss, (await Reads().ReadAsync("getBook", Guid.NewGuid(), ["title"], Token)).Kind);
    }

    [Fact]
    public async Task ReadAsync_OperationNotDeclaredCacheable_Miss()
    {
        Assert.Equal(CachedAnswerKind.Miss, (await Reads().ReadAsync("getLabel", _book, ["text"], Token)).Kind);
    }

    [Fact]
    public async Task ReadAsync_ResourceSwitchedOff_Miss()
    {
        var reads = Reads(configuration: new() { [$"Caching:Owned:Resources:{Service}:book:Enabled"] = "false" });

        Assert.Equal(CachedAnswerKind.Miss, (await reads.ReadAsync("getBook", _book, ["title"], Token)).Kind);
    }

    [Fact]
    public async Task ReadAsync_RowThatIsItsOwnOwner_NamedOrUnnamedHitAnotherNamedMiss()
    {
        var library = Guid.NewGuid();
        await Store($"{Service}:library:{library}", ("v", "1"), ("o", $$"""{"organization":"{{library}}"}"""),
            ("d", $$$"""{"id":"{{{library}}}","type":"library","attributes":{"name":"Alexandria"}}"""));

        _context.OrganizationId = null;
        Assert.Equal(CachedAnswerKind.Hit, (await Reads().ReadAsync("getLibrary", library, ["name"], Token)).Kind);

        _context.OrganizationId = library;
        Assert.Equal(CachedAnswerKind.Hit, (await Reads().ReadAsync("getLibrary", library, ["name"], Token)).Kind);

        _context.OrganizationId = Theirs;
        Assert.Equal(CachedAnswerKind.Miss, (await Reads().ReadAsync("getLibrary", library, ["name"], Token)).Kind);
    }

    [Fact]
    public async Task ReadAsync_NothingRecordedUnderTheKey_SaysSo()
    {
        await Store($"{Service}:quota:{Ours}", ("v", "-1"), ("o", $$"""{"organization":"{{Ours}}"}"""), ("none", "1"));

        Assert.Equal(CachedAnswerKind.NothingRecorded, (await Reads().ReadAsync("listQuotas", Ours, ["limit"], Token)).Kind);
    }

    [Fact]
    public async Task ReadAsync_InsideAStatement_ComparesWithTheStatedOrganizationAndUsesItUp()
    {
        _context.OrganizationId = null;

        using (OutgoingCall.AboutOrganization(Ours))
        {
            Assert.Equal(CachedAnswerKind.Hit, (await Reads().ReadAsync("getBook", _book, ["title"], Token)).Kind);

            // Used up, as the call it replaced would have used it.
            Assert.Throws<InvalidOperationException>(() => OutgoingCall.Take());
        }
    }

    [Fact]
    public async Task ReadAsync_InsideAStatementOnAMiss_LeavesItForTheCall()
    {
        using (OutgoingCall.AboutOrganization(Theirs))
        {
            Assert.Equal(CachedAnswerKind.Miss, (await Reads().ReadAsync("getBook", _book, ["title"], Token)).Kind);
            Assert.Equal(Theirs, OutgoingCall.Take()!.Value.Organization);
        }
    }

    [Fact]
    public async Task ReadAsync_NoServiceCache_Miss()
    {
        Assert.Equal(CachedAnswerKind.Miss, (await Reads(connected: false).ReadAsync("getBook", _book, ["title"], Token)).Kind);
    }

    [Fact]
    public async Task ReadAsync_RedisUnreachable_MissQuickly()
    {
        await using var connection = new CachedReadsConnection("127.0.0.1:1,connectTimeout=100");
        var reads = Reads(connection: connection);

        var started = DateTime.UtcNow;
        var answer = await reads.ReadAsync("getBook", _book, ["title"], Token);

        Assert.Equal(CachedAnswerKind.Miss, answer.Kind);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
    }

    // Redis refusing the read (here, the key holds a string rather than an entry) is a miss, and the cache rests.
    [Fact]
    public async Task ReadAsync_RedisRefusesTheRead_MissAndRests()
    {
        await _redis.GetDatabase().StringSetAsync($"{Service}:book:{_book}", "not an entry");

        var answer = await Reads().ReadAsync("getBook", _book, ["title"], Token);

        Assert.Equal(CachedAnswerKind.Miss, answer.Kind);
        Assert.Null(await _connection.DatabaseAsync());
    }

    // A connection string StackExchange.Redis can't parse fails every read: each is a miss, never a failed call.
    [Fact]
    public async Task ReadAsync_ConnectionStringRedisCannotParse_Miss()
    {
        await using var connection = new CachedReadsConnection("localhost:6379,bogusoption=1");

        Assert.Equal(CachedAnswerKind.Miss, (await Reads(connection: connection).ReadAsync("getBook", _book, ["title"], Token)).Kind);
    }

    // The rest after a failure, like a read's patience, is measured on the host's clock: on one
    // that stands still a read waits for Redis however long it takes, so a test can rely on it.
    [Fact]
    public async Task DatabaseAsync_AfterAFailure_RestsUntilTheHostsClockPassesTheRest()
    {
        var time = new FakeTimeProvider();
        await using var connection = new CachedReadsConnection(redis.Hostname, time);
        Assert.NotNull(await connection.DatabaseAsync());

        connection.Failed();
        var resting = await connection.DatabaseAsync();
        time.Advance(CachedReadsConnection.Rest);
        var rested = await connection.DatabaseAsync();

        Assert.Null(resting);
        Assert.NotNull(rested);
    }

    [Fact]
    public void FromContract_PublishedReads_ReadByOperation()
    {
        var reads = CacheableRead.FromContract(new MemoryStream(Encoding.UTF8.GetBytes(Contract)));

        Assert.Equal(["getBook", "getBookOld", "getLibrary", "getLoan", "listOpenLoans", "listPermits", "listQuotas"], reads.Keys.Order());
        Assert.Equal(["shelfmark"], reads["getBook"].PerCaller!);
        Assert.Equal(1, reads["getBook"].Format);
        Assert.Equal(0, reads["getBookOld"].Format);
        Assert.True(reads["listPermits"].AcrossOwners);
        Assert.False(reads["listQuotas"].AcrossOwners);
        Assert.Equal("shelves.read", reads["getBook"].Scope);
        Assert.Equal("required", reads["getBook"].Organization);
        Assert.True(reads["getLibrary"].OwnerIsSelf);
        Assert.Equal(["title", "version"], reads["getBook"].Fields.Order());
    }

    // A cached read-many: the page asked for, sliced in the entry's order, each resource shaped to the fields asked for,
    // with the person's whole count, for the person the call carries.
    [Theory]
    [InlineData(1, 1, new[] { "Anathem" })]
    [InlineData(2, 1, new[] { "Cryptonomicon" })]
    [InlineData(2, 2, new[] { "Dune" })]
    [InlineData(1, 10, new[] { "Anathem", "Cryptonomicon", "Dune" })]
    [InlineData(4, 1, new string[0])]
    public async Task ReadManyAsync_APage_HitSlicedAndShaped(int number, int size, string[] titles)
    {
        var person = await AReadMany();

        var answer = await Reads(caller: "lending-desk").ReadManyAsync("listOpenLoans", number, size, ["title"], Token);

        Assert.Equal(CachedAnswerKind.Hit, answer.Kind);
        Assert.Equal(3, answer.Total);
        using var page = JsonDocument.Parse(answer.Resource!);
        Assert.Equal(titles, page.RootElement.EnumerateArray()
            .Select(loan => loan.GetProperty("attributes").GetProperty("title").GetString()));
        Assert.All(page.RootElement.EnumerateArray(), loan => Assert.False(loan.GetProperty("attributes").TryGetProperty("organization", out _)));
        Assert.Equal(titles.Length, answer.AsList<Loan>()!.Count);
        Assert.Equal(person, _context.UserId);
    }

    // With no fieldset, every attribute the entry holds, which is every attribute a read-many has.
    [Fact]
    public async Task ReadManyAsync_NoFieldset_EveryAttribute()
    {
        await AReadMany();

        var answer = await Reads(caller: "lending-desk").ReadManyAsync("listOpenLoans", 1, 1, [], Token);

        using var page = JsonDocument.Parse(answer.Resource!);
        Assert.True(page.RootElement[0].GetProperty("attributes").TryGetProperty("organization", out _));
    }

    // The read-many answers only a page of itself: a read of one, a page of an operation caching one resource, a call
    // carrying an organization, a field the entry doesn't hold, or an asserting caller the operation doesn't admit, all
    // miss.
    [Fact]
    public async Task ReadManyAsync_AnythingButAPageOfTheReadMany_Misses()
    {
        await AReadMany();
        var reads = Reads(caller: "lending-desk");

        Assert.Equal(CachedAnswerKind.Miss, (await reads.ReadManyAsync("listOpenLoans", 1, 10, ["fine"], Token)).Kind);
        Assert.Equal(CachedAnswerKind.Miss, (await reads.ReadCurrentAsync("listOpenLoans", ["title"], Token)).Kind);
        Assert.Equal(CachedAnswerKind.Miss, (await reads.ReadManyAsync("getLoan", 1, 10, ["borrower"], Token)).Kind);
        Assert.Equal(CachedAnswerKind.Miss, (await Reads().ReadManyAsync("listOpenLoans", 1, 10, ["title"], Token)).Kind);
        _context.OrganizationId = Ours;
        Assert.Equal(CachedAnswerKind.Miss, (await reads.ReadManyAsync("listOpenLoans", 1, 10, ["title"], Token)).Kind);
    }

    // Another person's entry is never this person's: the owner is checked as for any read.
    [Fact]
    public async Task ReadManyAsync_AnEntryOwnedBySomebodyElse_Misses()
    {
        var person = await AReadMany(owner: Guid.NewGuid());

        Assert.Equal(CachedAnswerKind.Miss, (await Reads(caller: "lending-desk").ReadManyAsync("listOpenLoans", 1, 10, ["title"], Token)).Kind);
        Assert.Equal(person, _context.UserId);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    public async Task ReadManyAsync_APageBeforeTheFirstOrOfNone_Refused(int number, int size) =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Reads().ReadManyAsync("listOpenLoans", number, size, [], Token));

    [Fact]
    public void FromContract_AReadMany_SaysSoWithTheMostAnEntryHolds()
    {
        var reads = CacheableRead.FromContract(new MemoryStream(Encoding.UTF8.GetBytes(Contract)));

        Assert.True(reads["listOpenLoans"].ReadMany);
        Assert.Equal(200, reads["listOpenLoans"].AtMost);
        Assert.False(reads["getBook"].ReadMany);
        Assert.Null(reads["getBook"].AtMost);
    }

    // Every resource of the entry, sized by the contract rather than by the caller: no caller's page size can make a hit
    // part of the list.
    [Fact]
    public async Task ReadAllAsync_AReadMany_TheWholeEntry()
    {
        await AReadMany();

        var answer = await Reads(caller: "lending-desk").ReadAllAsync("listOpenLoans", ["title"], Token);

        Assert.Equal(CachedAnswerKind.Hit, answer.Kind);
        Assert.Equal(3, answer.Total);
        Assert.Equal(3, answer.AsList<Loan>()!.Count);
    }

    // An operation the contract publishes as no read-many, or doesn't publish, has no whole entry to read.
    [Theory]
    [InlineData("getLoan")]
    [InlineData("listNothing")]
    public async Task ReadAllAsync_NoReadMany_Misses(string operation)
    {
        await AReadMany();

        Assert.Equal(CachedAnswerKind.Miss, (await Reads(caller: "lending-desk").ReadAllAsync(operation, [], Token)).Kind);
    }

    /// <summary>
    /// A person's read-many of three loans, as the owner fills it; the context is the person's, with no organization.
    /// </summary>
    private async Task<Guid> AReadMany(Guid? owner = null)
    {
        var person = Guid.NewGuid();
        _context.OrganizationId = null;
        _context.UserId = person;
        await Store(
            $"{Service}:open-loan:read-many:{person}",
            ("v", "0"),
            ("o", $$"""{"user":"{{owner ?? person}}"}"""),
            ("d", """[{"id":"a","type":"open-loan","attributes":{"title":"Anathem","organization":"o"}},{"id":"b","type":"open-loan","attributes":{"title":"Cryptonomicon","organization":"o"}},{"id":"c","type":"open-loan","attributes":{"title":"Dune","organization":"o"}}]"""));
        return person;
    }

    private sealed class Loan
    {
        [Newtonsoft.Json.JsonProperty("id")]
        public string? Id { get; set; }
    }

    private CachedReads<CachedReadsTests> Reads(
        string[]? scopes = null,
        Dictionary<string, string?>? configuration = null,
        bool connected = true,
        CachedReadsConnection? connection = null,
        string caller = "")
    {
        var services = new ServiceCollection()
            .AddMetrics()
            .AddSingleton<IContext>(_context)
            .Configure<BitwardenClientOptions>(Service, options =>
            {
                options.Scopes = scopes ?? ["shelves.read"];
                options.CallerName = caller;
            })
            .BuildServiceProvider();

        return new CachedReads<CachedReadsTests>(
            Service,
            CacheableRead.FromContract(new MemoryStream(Encoding.UTF8.GetBytes(Contract))),
            connection ?? (connected ? _connection : null),
            services.GetRequiredService<IOptionsMonitor<BitwardenClientOptions>>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().AddInMemoryCollection(configuration ?? []).Build(),
            NullLogger<CachedReads<CachedReadsTests>>.Instance,
            new RequestMemoMetrics(services.GetRequiredService<IMeterFactory>()));
    }

    private async Task Store(string key, params (string Name, string Value)[] fields)
    {
        var database = _redis.GetDatabase();
        await database.KeyDeleteAsync(key);

        // An entry carries its format, as the owner writes it, unless the test says otherwise.
        if (!fields.Any(field => field.Name == OwnedCacheEntry.Format))
        {
            fields = [.. fields, (OwnedCacheEntry.Format, "1")];
        }

        await database.HashSetAsync(key, [.. fields.Select(field => new HashEntry(field.Name, field.Value))]);
    }

    private sealed class MutableContext : IContext
    {
        public Guid? OrganizationId { get; set; }

        public Guid? UserId { get; set; }

        public Guid? ProviderId { get; set; }
    }
}
