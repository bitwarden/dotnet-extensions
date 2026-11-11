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
              "fields": [ "title", "version" ] } } },
            "/api/v1/libraries/{id}": { "get": { "operationId": "getLibrary", "x-bitwarden-cache": {
              "resource": "library", "key": "shelf-service:library:{key}", "keyedBy": "id", "scope": "shelves.read",
              "context": { "organization": "optional", "user": "optional", "provider": "optional" },
              "owner": { "axis": "organization", "self": true },
              "fields": [ "name" ] } } },
            "/api/v1/quotas": { "get": { "operationId": "listQuotas", "x-bitwarden-cache": {
              "resource": "quota", "key": "shelf-service:quota:{key}", "keyedBy": "organization", "scope": "shelves.read",
              "context": { "organization": "optional", "user": "optional", "provider": "optional" },
              "owner": { "axis": "organization", "self": false },
              "fields": [ "limit" ] } } },
            "/api/v1/permits": { "get": { "operationId": "listPermits", "x-bitwarden-cache": {
              "resource": "permit", "key": "shelf-service:permit:{key}", "keyedBy": "organization", "scope": "shelves.read",
              "context": { "organization": "optional", "user": "optional", "provider": "optional" },
              "owner": { "axis": "organization", "self": false, "acrossOwners": true },
              "fields": [ "limit" ] } } },
            "/api/v1/loans/{id}": { "get": { "operationId": "getLoan", "x-bitwarden-cache": {
              "resource": "loan", "key": "shelf-service:loan:{key}", "keyedBy": "id", "scope": "shelves.read",
              "context": { "organization": "required", "user": "optional", "provider": "optional" },
              "owner": { "axis": "organization", "self": false },
              "fields": [ "organization", "borrower", "returned" ] } } },
            "/api/v1/loans": { "get": { "operationId": "listLoans", "x-bitwarden-index": {
              "name": "loans-of", "resource": "loan",
              "key": "shelf-service:index:loans-of:{subject}:{generation}",
              "generation": "shelf-service:gen:loans-of:{subject}",
              "subject": { "axis": "user", "filter": "borrower", "attribute": "borrower" },
              "where": { "returned": [ "false" ] },
              "scope": "shelves.read",
              "context": { "organization": "forbidden", "user": "required", "provider": "optional" } } } },
            "/api/v1/holds": { "get": { "operationId": "listHolds", "x-bitwarden-index": {
              "name": "loans-here", "resource": "loan",
              "key": "shelf-service:index:loans-here:{owner}:{subject}:{generation}",
              "generation": "shelf-service:gen:loans-here:{subject}",
              "subject": { "axis": "user", "filter": "borrower", "attribute": "borrower" },
              "within": "organization",
              "carries": [ "suspended" ],
              "where": { },
              "scope": "shelves.read",
              "context": { "organization": "required", "user": "required", "provider": "optional" } } } },
            "/api/v1/labels/{id}": { "get": { "operationId": "getLabel" } },
            "/api/v1/borrowers/presented/actions/standing": { "post": { "operationId": "standing", "x-bitwarden-answer": [
              { "name": "standing-of", "key": "shelf-service:answer:standing-of:{key}", "keyedBy": "borrower", "digest": null,
                "scope": "shelves.read", "context": { "organization": "forbidden", "user": "forbidden", "provider": "optional" } },
              { "name": "standing-by-label", "key": "shelf-service:answer:standing-by-label:{key}", "keyedBy": "label",
                "digest": "sha-256", "scope": "shelves.read",
                "context": { "organization": "forbidden", "user": "forbidden", "provider": "optional" } },
              { "name": "standing-by-shelfmark", "key": "shelf-service:answer:standing-by-shelfmark:{key}", "keyedBy": "shelfmark",
                "digest": "sha-3", "scope": "shelves.read",
                "context": { "organization": "forbidden", "user": "forbidden", "provider": "optional" } } ] } }
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
    public async Task ReadAsync_AnUnreadableOwner_Miss(string owner)
    {
        await Store($"{Service}:quota:{Ours}", ("v", "1"), ("o", owner),
            ("d", $$$"""{"id":"{{{Ours}}}","type":"quota","attributes":{"limit":3}}"""));
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

        Assert.Equal(["getBook", "getLibrary", "getLoan", "listPermits", "listQuotas"], reads.Keys.Order());
        Assert.True(reads["listPermits"].AcrossOwners);
        Assert.False(reads["listQuotas"].AcrossOwners);
        var indexes = CacheableIndexRead.FromContract(new MemoryStream(Encoding.UTF8.GetBytes(Contract)));
        Assert.Equal("organization", indexes["listHolds/loans-here"].Within);
        Assert.Contains("suspended", indexes["listHolds/loans-here"].Carries);
        Assert.Null(indexes["listLoans/loans-of"].Within);
        Assert.Equal("shelves.read", reads["getBook"].Scope);
        Assert.Equal("required", reads["getBook"].Organization);
        Assert.True(reads["getLibrary"].OwnerIsSelf);
        Assert.Equal(["title", "version"], reads["getBook"].Fields.Order());
    }

    [Fact]
    public async Task ReadIndexAsync_ThePrincipalsOwnAnswer_EveryResourceShaped()
    {
        var (person, first, second) = await IndexedAsync();
        _context.OrganizationId = null;
        _context.UserId = person;

        var answer = await Reads().ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token);

        Assert.NotNull(answer);
        Assert.Equal(2, answer.Count);
        Assert.Contains(answer, loan => loan.Contains(first.ToString()));
        Assert.All(answer, loan => Assert.DoesNotContain("borrower", loan));
        Assert.Contains(answer, loan => loan.Contains(second.ToString()));
    }

    [Fact]
    public async Task ReadIndexAsync_AnotherPrincipal_Miss()
    {
        var (person, _, _) = await IndexedAsync();
        _context.OrganizationId = null;
        _context.UserId = Guid.NewGuid();

        Assert.Null(await Reads().ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token));
    }

    [Fact]
    public async Task ReadIndexAsync_CallNamingAnOrganization_Miss()
    {
        var (person, _, _) = await IndexedAsync();
        _context.UserId = person;

        Assert.Null(await Reads().ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token));
    }

    [Fact]
    public async Task ReadIndexAsync_GenerationMovedOn_Miss()
    {
        var (person, _, _) = await IndexedAsync();
        _context.OrganizationId = null;
        _context.UserId = person;
        await _redis.GetDatabase().HashIncrementAsync($"{Service}:gen:loans-of:{person}", "g");

        Assert.Null(await Reads().ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token));
    }

    [Fact]
    public async Task ReadIndexAsync_AResourceNoLongerAnswering_Miss()
    {
        var (person, first, _) = await IndexedAsync();
        _context.OrganizationId = null;
        _context.UserId = person;
        await Store($"{Service}:loan:{first}", ("v", "2"), ("o", $$"""{"organization":"{{Ours}}"}"""),
            ("d", $$$"""{"id":"{{{first}}}","type":"loan","attributes":{"organization":"{{{Ours}}}","borrower":"{{{person}}}","returned":true}}"""));

        Assert.Null(await Reads().ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token));
    }

    // An index naming something other than identifiers is unreadable: a miss, never a failed call.
    [Fact]
    public async Task ReadIndexAsync_AnIndexOfTheWrongShape_Miss()
    {
        var (person, _, _) = await IndexedAsync();
        _context.OrganizationId = null;
        _context.UserId = person;
        await _redis.GetDatabase().StringSetAsync($"{Service}:index:loans-of:{person}:7", """{"ids":[7]}""");

        Assert.Null(await Reads().ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token));
    }

    [Fact]
    public async Task ReadIndexAsync_AResourceGone_Miss()
    {
        var (person, _, second) = await IndexedAsync();
        _context.OrganizationId = null;
        _context.UserId = person;
        await Store($"{Service}:loan:{second}", ("v", "2"), ("gone", "1"));

        Assert.Null(await Reads().ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token));
    }

    [Fact]
    public async Task ReadIndexAsync_InsideTheCallsOrganization_CarriesTheFactAskedFor()
    {
        var (person, first, _) = await IndexedAsync();
        _context.OrganizationId = Ours;
        _context.UserId = person;

        var answer = await Reads().ReadIndexAsync("listHolds", "loans-here", person, ["returned", "suspended"], Token);

        var loan = JsonDocument.Parse(Assert.Single(answer!)).RootElement;
        Assert.Equal(first.ToString(), loan.GetProperty("id").GetString());
        Assert.True(loan.GetProperty("attributes").GetProperty("suspended").GetBoolean());
        Assert.False(loan.GetProperty("attributes").GetProperty("returned").GetBoolean());
    }

    [Fact]
    public async Task ReadIndexAsync_InsideAnotherOrganization_Miss()
    {
        var (person, _, _) = await IndexedAsync();
        _context.OrganizationId = Theirs;
        _context.UserId = person;

        Assert.Null(await Reads().ReadIndexAsync("listHolds", "loans-here", person, ["returned"], Token));
    }

    [Fact]
    public async Task ReadIndexAsync_ReadAgainUnchanged_AnsweredFromWhatThisProcessHolds()
    {
        var (person, first, _) = await IndexedAsync();
        _context.OrganizationId = null;
        _context.UserId = person;
        var reads = Reads();
        await reads.ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token);

        // Same version, different bytes: an owner never writes this, so seeing the old bytes shows
        // the second read confirmed what it held rather than reading the entry again.
        await Store($"{Service}:loan:{first}", ("v", "1"), ("o", $$"""{"organization":"{{Ours}}"}"""),
            ("d", $$$"""{"id":"{{{first}}}","type":"loan","attributes":{"organization":"{{{Theirs}}}","borrower":"{{{person}}}","returned":false}}"""));

        var answer = await reads.ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token);

        Assert.Contains(answer!, loan => loan.Contains(first.ToString()) && loan.Contains(Ours.ToString()));
    }

    [Fact]
    public async Task ReadIndexAsync_ReadAgainSwitchedOff_ReadFromRedisEachTime()
    {
        var (person, first, _) = await IndexedAsync();
        _context.OrganizationId = null;
        _context.UserId = person;
        var reads = Reads(configuration: new() { [NearIndexAnswers.EnabledKey] = "false" });
        await reads.ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token);
        await Store($"{Service}:loan:{first}", ("v", "1"), ("o", $$"""{"organization":"{{Ours}}"}"""),
            ("d", $$$"""{"id":"{{{first}}}","type":"loan","attributes":{"organization":"{{{Theirs}}}","borrower":"{{{person}}}","returned":false}}"""));

        var answer = await reads.ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token);

        Assert.Contains(answer!, loan => loan.Contains(first.ToString()) && loan.Contains(Theirs.ToString()));
    }

    [Fact]
    public async Task ReadIndexAsync_GenerationMovedOnAfterARead_Miss()
    {
        var (person, _, _) = await IndexedAsync();
        _context.OrganizationId = null;
        _context.UserId = person;
        var reads = Reads();
        Assert.NotNull(await reads.ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token));

        await _redis.GetDatabase().HashIncrementAsync($"{Service}:gen:loans-of:{person}", "g");

        Assert.Null(await reads.ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token));
    }

    [Fact]
    public async Task ReadIndexAsync_AResourceGoneAfterARead_Miss()
    {
        var (person, _, second) = await IndexedAsync();
        _context.OrganizationId = null;
        _context.UserId = person;
        var reads = Reads();
        Assert.NotNull(await reads.ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token));

        await Store($"{Service}:loan:{second}", ("v", "2"), ("gone", "1"));

        Assert.Null(await reads.ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token));
    }

    [Fact]
    public async Task ReadIndexAsync_AResourceRefilledAfterARead_TheNewVersion()
    {
        var (person, first, _) = await IndexedAsync();
        _context.OrganizationId = Ours;
        _context.UserId = person;
        var reads = Reads();
        Assert.NotNull(await reads.ReadIndexAsync("listHolds", "loans-here", person, ["returned"], Token));

        await Store($"{Service}:loan:{first}", ("v", "2"), ("o", $$"""{"organization":"{{Ours}}"}"""),
            ("d", $$$"""{"id":"{{{first}}}","type":"loan","attributes":{"organization":"{{{Ours}}}","borrower":"{{{person}}}","returned":true}}"""));

        // The index still names it, so the answer stands; its new version is what is read.
        var loan = JsonDocument.Parse(Assert.Single((await reads.ReadIndexAsync("listHolds", "loans-here", person, ["returned"], Token))!)).RootElement;

        Assert.True(loan.GetProperty("attributes").GetProperty("returned").GetBoolean());
    }

    [Fact]
    public async Task ReadIndexAsync_TheIndexEntryGoneAfterARead_Miss()
    {
        var (person, _, _) = await IndexedAsync();
        _context.OrganizationId = null;
        _context.UserId = person;
        var reads = Reads();
        Assert.NotNull(await reads.ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token));

        await _redis.GetDatabase().KeyDeleteAsync($"{Service}:index:loans-of:{person}:7");

        Assert.Null(await reads.ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token));
    }

    [Fact]
    public async Task ReadIndexAsync_ReadAgainByAnotherPrincipal_Miss()
    {
        var (person, _, _) = await IndexedAsync();
        _context.OrganizationId = null;
        _context.UserId = person;
        var reads = Reads();
        Assert.NotNull(await reads.ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token));

        _context.UserId = Guid.NewGuid();

        Assert.Null(await reads.ReadIndexAsync("listLoans", "loans-of", person, ["organization"], Token));
    }

    /// <summary>
    /// A person with two open loans in two organizations, indexed at generation 7.
    /// </summary>
    private async Task<(Guid Person, Guid First, Guid Second)> IndexedAsync()
    {
        var person = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var database = _redis.GetDatabase();

        foreach (var (loan, organization) in new[] { (first, Ours), (second, Theirs) })
        {
            await Store($"{Service}:loan:{loan}", ("v", "1"), ("o", $$"""{"organization":"{{organization}}"}"""),
                ("d", $$$"""{"id":"{{{loan}}}","type":"loan","attributes":{"organization":"{{{organization}}}","borrower":"{{{person}}}","returned":false}}"""));
        }

        await database.HashSetAsync($"{Service}:gen:loans-of:{person}", "g", 7);
        await database.StringSetAsync($"{Service}:index:loans-of:{person}:7", $$$"""{"ids":["{{{first}}}","{{{second}}}"],"carried":{}}""");
        await database.HashSetAsync($"{Service}:gen:loans-here:{person}", "g", 3);
        await database.StringSetAsync(
            $"{Service}:index:loans-here:{Ours}:{person}:3", $$$"""{"ids":["{{{first}}}"],"carried":{"suspended":true}}""");

        return (person, first, second);
    }

    [Fact]
    public async Task ReadAnswer_Kept_HitWithTheBody()
    {
        var borrower = Guid.NewGuid();
        await Store($"{Service}:answer:standing-of:{borrower}", ("v", "3"), ("d", """{"value":"borrowing"}"""));
        _context.OrganizationId = null;

        var answer = await Reads().ReadAnswerAsync("standing", "standing-of", borrower.ToString(), Token);

        Assert.Equal(CachedAnswerKind.Hit, answer.Kind);
        Assert.Equal("""{"value":"borrowing"}""", answer.Resource);
    }

    [Fact]
    public async Task ReadAnswer_Nothing_NothingRecorded()
    {
        var borrower = Guid.NewGuid();
        await Store($"{Service}:answer:standing-of:{borrower}", ("v", "0"), ("none", "1"));
        _context.OrganizationId = null;

        var answer = await Reads().ReadAnswerAsync("standing", "standing-of", borrower.ToString(), Token);

        Assert.Equal(CachedAnswerKind.NothingRecorded, answer.Kind);
    }

    [Fact]
    public async Task ReadAnswer_Tombstone_Miss()
    {
        var borrower = Guid.NewGuid();
        await Store($"{Service}:answer:standing-of:{borrower}", ("v", "4"), ("gone", "1"));
        _context.OrganizationId = null;

        var answer = await Reads().ReadAnswerAsync("standing", "standing-of", borrower.ToString(), Token);

        Assert.Equal(CachedAnswerKind.Miss, answer.Kind);
    }

    [Fact]
    public async Task ReadAnswer_TextKey_ReadByItsDigest()
    {
        await Store($"{Service}:answer:standing-by-label:{OwnedAnswerKey.Of("standing-by-label", "rare")}", ("v", "0"), ("d", """{"value":"labelled"}"""));
        _context.OrganizationId = null;

        var answer = await Reads().ReadAnswerAsync("standing", "standing-by-label", "rare", Token);

        Assert.Equal(CachedAnswerKind.Hit, answer.Kind);
    }

    [Fact]
    public async Task ReadAnswer_TextKey_WithTheOwnersSecret_ReadByItsKeyedDigest()
    {
        var secret = Enumerable.Repeat((byte)7, 32).ToArray();
        await Store($"{Service}:answer:standing-by-label:{OwnedAnswerKey.Of("standing-by-label", "rare", secret)}", ("v", "0"), ("d", """{"value":"labelled"}"""));
        _context.OrganizationId = null;

        var answer = await Reads(configuration: new() { [OwnedAnswerKey.SecretKey] = Convert.ToBase64String(secret) })
            .ReadAnswerAsync("standing", "standing-by-label", "rare", Token);

        Assert.Equal(CachedAnswerKind.Hit, answer.Kind);
    }

    [Fact]
    public async Task ReadAnswer_TextKey_WithoutTheOwnersSecret_Miss()
    {
        var secret = Enumerable.Repeat((byte)7, 32).ToArray();
        await Store($"{Service}:answer:standing-by-label:{OwnedAnswerKey.Of("standing-by-label", "scarce", secret)}", ("v", "0"), ("d", """{"value":"labelled"}"""));
        _context.OrganizationId = null;

        var answer = await Reads().ReadAnswerAsync("standing", "standing-by-label", "scarce", Token);

        Assert.Equal(CachedAnswerKind.Miss, answer.Kind);
    }

    [Fact]
    public async Task ReadAnswer_DigestThisClientDoesNotKnow_Miss()
    {
        _context.OrganizationId = null;

        var answer = await Reads().ReadAnswerAsync("standing", "standing-by-shelfmark", "QA 76", Token);

        Assert.Equal(CachedAnswerKind.Miss, answer.Kind);
    }

    [Fact]
    public async Task ReadAnswer_ContextTheActionForbids_Miss()
    {
        var borrower = Guid.NewGuid();
        await Store($"{Service}:answer:standing-of:{borrower}", ("v", "3"), ("d", """{"value":"borrowing"}"""));

        // The call names an organization, which the action refuses: the service wouldn't answer it.
        var answer = await Reads().ReadAnswerAsync("standing", "standing-of", borrower.ToString(), Token);

        Assert.Equal(CachedAnswerKind.Miss, answer.Kind);
    }

    [Fact]
    public async Task ReadAnswer_ScopeNotHeld_Miss()
    {
        var borrower = Guid.NewGuid();
        await Store($"{Service}:answer:standing-of:{borrower}", ("v", "3"), ("d", """{"value":"borrowing"}"""));
        _context.OrganizationId = null;

        var answer = await Reads(scopes: ["shelves.write"]).ReadAnswerAsync("standing", "standing-of", borrower.ToString(), Token);

        Assert.Equal(CachedAnswerKind.Miss, answer.Kind);
    }

    [Fact]
    public async Task ReadAnswer_SwitchedOff_Miss()
    {
        var borrower = Guid.NewGuid();
        await Store($"{Service}:answer:standing-of:{borrower}", ("v", "3"), ("d", """{"value":"borrowing"}"""));
        _context.OrganizationId = null;

        var answer = await Reads(configuration: new() { [$"Caching:Owned:Resources:{Service}:standing-of:Enabled"] = "false" })
            .ReadAnswerAsync("standing", "standing-of", borrower.ToString(), Token);

        Assert.Equal(CachedAnswerKind.Miss, answer.Kind);
    }

    private CachedReads<CachedReadsTests> Reads(
        string[]? scopes = null,
        Dictionary<string, string?>? configuration = null,
        bool connected = true,
        CachedReadsConnection? connection = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<IContext>(_context)
            .Configure<BitwardenClientOptions>(Service, options => options.Scopes = scopes ?? ["shelves.read"])
            .BuildServiceProvider();

        return new CachedReads<CachedReadsTests>(
            Service,
            CacheableRead.FromContract(new MemoryStream(Encoding.UTF8.GetBytes(Contract))),
            CacheableIndexRead.FromContract(new MemoryStream(Encoding.UTF8.GetBytes(Contract))),
            connection ?? (connected ? _connection : null),
            services.GetRequiredService<IOptionsMonitor<BitwardenClientOptions>>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().AddInMemoryCollection(configuration ?? []).Build(),
            NullLogger<CachedReads<CachedReadsTests>>.Instance,
            CacheableAnswerRead.FromContract(new MemoryStream(Encoding.UTF8.GetBytes(Contract))));
    }

    private async Task Store(string key, params (string Name, string Value)[] fields)
    {
        var database = _redis.GetDatabase();
        await database.KeyDeleteAsync(key);
        await database.HashSetAsync(key, [.. fields.Select(field => new HashEntry(field.Name, field.Value))]);
    }

    private sealed class MutableContext : IContext
    {
        public Guid? OrganizationId { get; set; }

        public Guid? UserId { get; set; }

        public Guid? ProviderId { get; set; }
    }
}
