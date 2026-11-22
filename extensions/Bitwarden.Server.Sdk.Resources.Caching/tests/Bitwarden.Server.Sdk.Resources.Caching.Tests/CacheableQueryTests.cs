using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Resources.Caching.Tests;

/// <summary>
/// A read made cacheable by its query: the service's own entry answers it as a client would, the whole resource is
/// filled less what is worked out per caller, and a read a client couldn't repeat is refused as the host starts.
/// </summary>
/// <remarks>
/// Compiled with the request delegate generator in Bitwarden.Server.Sdk.Resources.Caching.Generated.Tests as well, so
/// the refusals and the reads hold on both of the framework's code paths.
/// </remarks>
public sealed class CacheableQueryTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
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

    // The second read is the entry's: changed in Redis behind the service's back, the change is what comes back.
    [Fact]
    public async Task Get_Twice_TheSecondIsAnsweredFromTheServicesOwnEntry()
    {
        await using var host = await StartAsync();
        var first = await BodyAsync(host, $"/api/v1/books/{_book}?fields[book]=title,version");
        await Redis.HashSetAsync(BookKey, OwnedCacheEntry.Data, (await Redis.HashGetAsync(BookKey, OwnedCacheEntry.Data)).ToString().Replace("Dune", "From the entry", StringComparison.Ordinal));

        var second = await BodyAsync(host, $"/api/v1/books/{_book}?fields[book]=title,version");

        Assert.Contains("\"Dune\"", first);
        Assert.Contains("\"From the entry\"", second);
    }

    // What the entry answers is exactly what the database answers.
    [Fact]
    public async Task Get_FromTheEntry_IsTheSameResponseAsFromTheDatabase()
    {
        await using var host = await StartAsync();

        var fromDatabase = await BodyAsync(host, $"/api/v1/books/{_book}?fields[book]=title,inscription,version");
        Assert.True(await Redis.KeyExistsAsync(BookKey));
        var fromEntry = await BodyAsync(host, $"/api/v1/books/{_book}?fields[book]=title,inscription,version");

        Assert.Equal(fromDatabase, fromEntry);
    }

    // A read asking for an attribute worked out per caller, or for every attribute, is answered from the database.
    [Theory]
    [InlineData("?fields[book]=title,shelfmark")]
    [InlineData("")]
    public async Task Get_AskingForWhatIsWorkedOutPerCaller_IsTheDatabasesAnswerWithIt(string query)
    {
        await using var host = await StartAsync();
        (await host.GetAsync($"/api/v1/books/{_book}", Ours)).Dispose();
        await Redis.HashSetAsync(BookKey, OwnedCacheEntry.Data, (await Redis.HashGetAsync(BookKey, OwnedCacheEntry.Data)).ToString().Replace("Dune", "From the entry", StringComparison.Ordinal));

        var body = await BodyAsync(host, $"/api/v1/books/{_book}{query}");

        Assert.Contains("\"Dune\"", body);
        Assert.Contains("\"A-1\"", body);
    }

    // An entry in a format this release doesn't know, or of another owner, is a miss: the database answers.
    [Theory]
    [InlineData(OwnedCacheEntry.Format, "0")]
    [InlineData(OwnedCacheEntry.Owner, "{\"organization\":\"00000000-0000-0000-0000-000000000001\"}")]
    public async Task Get_EntryAClientWouldNotUse_IsAMiss(string field, string value)
    {
        await using var host = await StartAsync();
        (await host.GetAsync($"/api/v1/books/{_book}", Ours)).Dispose();
        await Redis.HashSetAsync(BookKey, [
            new HashEntry(OwnedCacheEntry.Data, (await Redis.HashGetAsync(BookKey, OwnedCacheEntry.Data)).ToString().Replace("Dune", "From the entry", StringComparison.Ordinal)),
            new HashEntry(field, value),
        ]);

        var body = await BodyAsync(host, $"/api/v1/books/{_book}?fields[book]=title");

        Assert.Contains("\"Dune\"", body);
    }

    // The entry is the row's owner's: a call naming another organization is refused as the database refuses it.
    [Fact]
    public async Task Get_AnotherOrganizationNamed_NotFoundEvenWithAnEntry()
    {
        await using var host = await StartAsync();
        (await host.GetAsync($"/api/v1/books/{_book}", Ours)).Dispose();

        using var response = await host.GetAsync($"/api/v1/books/{_book}?fields[book]=title", Theirs);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // "Nothing recorded" under a key is the entry's answer too.
    [Fact]
    public async Task ReadByKey_NothingRecorded_TheEntrysNothing()
    {
        await using var host = await StartAsync();
        var key = $"{ShelfHost.ServiceName}:quota:by-organization:{Theirs}";
        (await host.GetAsync($"/api/v1/quotas/{Theirs}", Theirs)).Dispose();
        Assert.Equal("1", (string?)await Redis.HashGetAsync(key, OwnedCacheEntry.None));

        using var scope = host.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IGetQuotaQuery>()
            .ReadByKeyAsync(Theirs, cancellationToken: TestContext.Current.CancellationToken));
    }

    // Under a key that is the owner, a read naming another owner finds no row because row-level security hides it, not
    // because none is recorded: caching that as "nothing recorded" would answer the owner's own reads with it.
    [Fact]
    public async Task ReadByKey_ByAnotherOwner_CachesNothingAndTheOwnerStillReadsItsRow()
    {
        await using var host = await ShelfHost.StartAsync(redis.Service, services => services
            .AddQuery<IGetQuotaQuery, OwnQuotaQuery>()
            .AddOwnedCache<ShelfContext>());
        var owner = Guid.NewGuid();
        var key = $"{ShelfHost.ServiceName}:quota:by-organization:{owner}";
        await host.InScopeAsync(owner, async database =>
        {
            database.Quotas.Add(new QuotaEntity { Id = Guid.NewGuid(), OrganizationId = owner, Limit = 2, Version = 1 });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });
        await Redis.KeyDeleteAsync(key);

        using (var another = await host.GetAsync($"/api/v1/quotas/{owner}?fields[quota]=limit", Theirs))
        {
            Assert.Equal(HttpStatusCode.NotFound, another.StatusCode);
        }

        Assert.False(await Redis.KeyExistsAsync(key));
        using var own = await host.GetAsync($"/api/v1/quotas/{owner}?fields[quota]=limit", owner);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    [Theory]
    [InlineData("filtered", "has endpoint filters")]
    [InlineData("with-more", "takes 'database'")]
    [InlineData("by-policy", "requirement or policy of its own")]
    [InlineData("any-scope", "accepts any of several scopes")]
    [InlineData("anonymous", "is anonymous")]
    [InlineData("self-authorized", "authorizes the caller itself")]
    public async Task Start_ReadAClientCouldNotRepeat_Refused(string refusal, string expected)
    {
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => ShelfHost.StartAsync(redis.Service, services => services
            .AddSingleton(new BookRefusal(refusal))
            .AddQuery<IGetBookQuery, GetBookQuery>()
            .AddOwnedCache<ShelfContext>()));

        Assert.Contains(expected, refused.Message);
    }

    // A host calling AddValidation gives every endpoint the framework's validation filter, which is the platform's own
    // and not one a cacheable read may not have: the read starts, and its second answer is the entry's.
    [Fact]
    public async Task Start_HostCallingAddValidation_CacheableReadStarts()
    {
        await using var host = await StartAsync(validation: true);

        await BodyAsync(host, $"/api/v1/books/{_book}?fields[book]=title");

        Assert.True(await Redis.KeyExistsAsync(BookKey));
    }

    [Fact]
    public async Task Start_ReadDemandingARole_Refused()
    {
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => ShelfHost.StartAsync(redis.Service, services =>
            services.AddQuery<IGetGuardedQuery, GetGuardedQuery>().AddOwnedCache<ShelfContext>()));

        Assert.Contains("demands the Owner role", refusal.Message);
    }

    [Fact]
    public async Task Start_RowWithAQueryFilterOfItsOwn_Refused()
    {
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => ShelfHost.StartAsync(redis.Service, services =>
            services.AddQuery<IGetArchiveQuery, GetArchiveQuery>().AddOwnedCache<ShelfContext>()));

        Assert.Contains("has a query filter of its own", refusal.Message);
    }

    [Theory]
    [InlineData(typeof(FilteredShelfQuery), "a filtered include")]
    [InlineData(typeof(NarrowedShelfQuery), "shapes its query with Where")]
    [InlineData(typeof(DeepShelfQuery), "not a navigation of the cached row itself")]
    public async Task Start_ReadShapedBeyondIncludes_Refused(Type query, string expected)
    {
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => ShelfHost.StartAsync(redis.Service, services =>
        {
            services.AddScoped(typeof(IGetShelfQuery), query);
            services.AddOwnedCache<ShelfContext>();
        }));

        Assert.Contains(expected, refusal.Message);
    }

    [Fact]
    public async Task Start_TwoQueriesOfOneTableAsTwoResources_Refused()
    {
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => ShelfHost.StartAsync(redis.Service, services => services
            .AddQuery<IGetBookQuery, GetBookQuery>()
            .AddQuery<IGetGuardedQuery, GetGuardedQuery>()
            .AddOwnedCache<ShelfContext>()));

        Assert.Contains("disagree on its version or its resource", refusal.Message);
    }

    // Two queries of one table each keep the row under a key of its own; two under one key would overwrite each other.
    [Fact]
    public async Task Start_TwoQueriesOfOneTableUnderOneKey_Refused()
    {
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => ShelfHost.StartAsync(redis.Service, services => services
            .AddQuery<IGetLoanQuery, GetLoanQuery>()
            .AddQuery<IGetBorrowersLoanQuery, LoanByIdentifierAgainQuery>()
            .AddOwnedCache<ShelfContext>()));

        Assert.Contains("under the same key", refusal.Message);
    }

    // Read across owners, an entry answers any holder of the scope: no personal data in it.
    [Fact]
    public void Declare_PersonalDataReadAcrossOwners_Refused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(ShelfQueries.Declare<IGetQuotaQuery, QuotaWithContactQuery>);

        Assert.Contains("personal data", refusal.Message);
    }

    [Fact]
    public void Declare_PersonalDataWorkedOutPerCaller_Declared()
    {
        var quota = ShelfQueries.Declare<IGetQuotaQuery, GetQuotaQuery>();

        Assert.DoesNotContain("contact", quota.Fields);
        Assert.Contains("limit", quota.Fields);
        Assert.Equal(["contact"], quota.PerCaller);
    }

    // Protected Data is cached as the database holds it, ciphertext: the entry is what any holder of the scope reads.
    [Fact]
    public void Declare_ProtectedDataAttribute_CachedAsTheResourceHoldsIt()
    {
        var book = ShelfQueries.Declare<IGetBookQuery, GetBookQuery>();

        Assert.Contains("inscription", book.Fields);
    }

    [Fact]
    public void Declare_PerCallerAttributeTheResourceDoesNotHave_Refused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(ShelfQueries.Declare<IGetBookQuery, MisnamedBookQuery>);

        Assert.Contains("not one of Book's attributes", refusal.Message);
    }

    [Fact]
    public void Declare_RowWithoutAVersion_Refused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(ShelfQueries.Declare<IGetLabelQuery, GetLabelQuery>);

        Assert.Contains("has no Version", refusal.Message);
    }

    private async Task<ShelfHost> StartAsync(bool validation = false)
    {
        var host = await ShelfHost.StartAsync(redis.Service, services =>
        {
            services
                .AddQuery<IGetBookQuery, GetBookQuery>()
                .AddQuery<IGetQuotaQuery, GetQuotaQuery>()
                .AddOwnedCache<ShelfContext>();

            if (validation)
            {
                services.AddValidation();
            }
        });

        await host.InScopeAsync(Ours, async database =>
        {
            database.Books.Add(new BookEntity { Id = _book, OrganizationId = Ours, Title = "Dune", Inscription = "2.iv|ct|mac", Version = 3 });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        return host;
    }

    private static async Task<string> BodyAsync(ShelfHost host, string path)
    {
        using var response = await host.GetAsync(path, Ours);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return document.RootElement.GetProperty("data").GetRawText();
    }
}
