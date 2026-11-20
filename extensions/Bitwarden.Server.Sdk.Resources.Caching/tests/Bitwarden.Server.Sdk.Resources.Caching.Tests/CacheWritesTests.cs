using System.Net;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Resources.Caching.Tests;

/// <summary>
/// A change the cache can't be told of before it commits: refused, with <c>503</c> and nothing committed, unless every
/// declaration it touches says to proceed (<see cref="CacheWrites"/>).
/// </summary>
public sealed class CacheWritesTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    /// <summary>
    /// A Redis nothing listens on.
    /// </summary>
    private const string Unreachable = "127.0.0.1:1,abortConnect=false,connectTimeout=200,syncTimeout=500,asyncTimeout=500,user=shelf-service,password=unused";

    private const string WriteScope = "shelves.write";

    private static readonly Guid Library = Guid.NewGuid();

    private readonly Guid _reader = Guid.NewGuid();
    private readonly Guid _loan = Guid.NewGuid();
    private ConnectionMultiplexer _redis = null!;

    public async ValueTask InitializeAsync() => _redis = await ConnectionMultiplexer.ConnectAsync(redis.Hostname);

    public async ValueTask DisposeAsync() => await _redis.DisposeAsync();

    [Fact]
    public async Task Return_CacheUnreachable_Answers503AndCommitsNothing()
    {
        await using var host = await StartAsync(Unreachable);

        using var response = await PostAsync(host, $"/api/v1/loans/{_loan}/actions/return");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("could not be told of the change", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False(await ReturnedAsync(host));
    }

    [Fact]
    public async Task Return_InATransaction_CacheUnreachable_Answers503AndCommitsNothing()
    {
        await using var host = await StartAsync(Unreachable);

        using var response = await PostAsync(host, $"/api/v1/loans/{_loan}/actions/return-in-transaction");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(await ReturnedAsync(host));
    }

    [Fact]
    public async Task Delete_CacheUnreachable_Answers503AndKeepsTheRow()
    {
        await using var host = await StartAsync(Unreachable);

        using var response = await SendAsync(host, HttpMethod.Delete, $"/api/v1/loans/{_loan}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await host.InScopeAsync(Library, async database =>
            Assert.True(await database.Loans.AnyAsync(loan => loan.Id == _loan, TestContext.Current.CancellationToken)));
    }

    // Outside a request (a message handler, a job) the save itself fails, and nothing commits.
    [Fact]
    public async Task Save_OutsideARequest_CacheUnreachable_ThrowsAndCommitsNothing()
    {
        await using var host = await StartAsync(Unreachable);

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => host.InScopeAsync(Library, async database =>
        {
            (await database.Loans.SingleAsync(loan => loan.Id == _loan, TestContext.Current.CancellationToken)).Returned = true;
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        }));

        Assert.False(await ReturnedAsync(host));
    }

    // A commit refused, tried again on the same transaction, is refused again: never committed without the cache told.
    [Fact]
    public async Task Commit_RefusedThenTriedAgain_RefusedAgain()
    {
        await using var host = await StartAsync(Unreachable);

        await host.InScopeAsync(Library, async database =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            (await database.Loans.SingleAsync(loan => loan.Id == _loan, TestContext.Current.CancellationToken)).Returned = true;
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<ServiceUnavailableException>(() => transaction.CommitAsync(TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ServiceUnavailableException>(() => transaction.CommitAsync(TestContext.Current.CancellationToken));
        });

        Assert.False(await ReturnedAsync(host));
    }

    [Fact]
    public async Task Return_DeclaredToProceed_CacheUnreachable_CommitsWithAWarning()
    {
        await using var host = await StartAsync(Unreachable, CacheWrites.Proceed);

        using var response = await PostAsync(host, $"/api/v1/loans/{_loan}/actions/return");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(await ReturnedAsync(host));
        Assert.Contains(
            host.Services.GetFakeLogCollector().GetSnapshot(),
            record => record.Level == LogLevel.Warning && record.Message.Contains("CacheWrites.Proceed", StringComparison.Ordinal));
    }

    // Only Redis failing to answer lets a change declared to proceed commit; any other failure is a fault, and the
    // change fails with it.
    [Theory]
    [InlineData(typeof(TimeoutException), true)]
    [InlineData(typeof(ObjectDisposedException), true)]
    [InlineData(typeof(InvalidOperationException), false)]
    public async Task Save_DeclaredToProceed_StoreFails_CommitsOnlyWhenRedisDidNotAnswer(Type failure, bool commits)
    {
        await using var host = await StartAsync(Unreachable, CacheWrites.Proceed, new FailingStore((Exception)Activator.CreateInstance(failure, "store")!));

        var save = host.InScopeAsync(Library, async database =>
        {
            (await database.Loans.SingleAsync(loan => loan.Id == _loan, TestContext.Current.CancellationToken)).Returned = true;
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        if (commits)
        {
            await save;
        }
        else
        {
            Assert.IsType(failure, await Assert.ThrowsAnyAsync<Exception>(() => save));
        }

        Assert.Equal(commits, await ReturnedAsync(host));
    }

    [Fact]
    public async Task Return_CacheReached_MarksTheVersionItReplacedDead()
    {
        await using var host = await StartAsync(redis.Service);
        using var response = await PostAsync(host, $"/api/v1/loans/{_loan}/actions/return");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var entry = _redis.GetDatabase();
        Assert.Equal("1", (string?)await entry.HashGetAsync($"{ShelfHost.ServiceName}:loan:{_loan}", OwnedCacheEntry.Gone));
        Assert.Equal("1", (string?)await entry.HashGetAsync($"{ShelfHost.ServiceName}:loan:{_loan}", OwnedCacheEntry.Version));
    }

    // Two declarations reading one table that disagree: refusing wins, and the host says which applies as it starts.
    [Theory]
    [InlineData(true, "refused (503)")]
    [InlineData(false, "committed with a warning")]
    public async Task Start_DeclarationsOfATable_RefuseWinsAndIsSaid(bool alsoRefusing, string expected)
    {
        await using var host = await ShelfHost.StartAsync(redis.Service, services =>
        {
            services.AddFakeLogging();
            services.AddQuery<IGetLoanQuery, ProceedingLoanQuery>();
            if (alsoRefusing)
            {
                services.AddQuery<IGetBorrowersLoanQuery, GetBorrowersLoanQuery>();
            }

            services.AddOwnedCache<ShelfContext>();
        });

        var said = host.Services.GetFakeLogCollector().GetSnapshot().Select(record => record.Message).ToList();
        Assert.Contains(said, message => message.Contains("LoanEntity", StringComparison.Ordinal) && message.Contains(expected, StringComparison.Ordinal));
    }

    // A tombstone must outlive a fill read before its change, and an arrival's tombstone, living the commit allowance,
    // a fill read before its change committed.
    [Theory]
    [InlineData("00:00:10", false, "CommitAllowance")]
    [InlineData(null, true, "tombstones live")]
    public async Task Start_TimesATombstoneWouldNotOutlive_Refused(string? allowance, bool brief, string expected)
    {
        var settings = new Dictionary<string, string?>();
        if (allowance is not null)
        {
            settings["Caching:Owned:CommitAllowance"] = allowance;
        }

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => ShelfHost.StartAsync(
            redis.Service,
            services => (brief ? services.AddQuery<IGetLoanQuery, BriefLoanQuery>() : services.AddQuery<IGetLoanQuery, GetLoanQuery>())
                .AddOwnedCache<ShelfContext>(),
            settings: settings));

        Assert.Contains(expected, refusal.Message);
    }

    private async Task<ShelfHost> StartAsync(string cache, CacheWrites writes = CacheWrites.Refuse, IOwnedCacheStore? store = null)
    {
        var host = await ShelfHost.StartAsync(cache, services =>
        {
            services.AddFakeLogging();
            if (store is not null)
            {
                services.AddSingleton(store);
            }

            (writes == CacheWrites.Proceed
                    ? services.AddQuery<IGetLoanQuery, ProceedingLoanQuery>()
                    : services.AddQuery<IGetLoanQuery, GetLoanQuery>())
                .AddOwnedCache<ShelfContext>();
        });

        // A loan arriving by identifier replaced nothing in the cache: with the cache out of reach, the seed still saves.
        await host.InScopeAsync(Library, async database =>
        {
            database.Loans.Add(new LoanEntity { Id = _loan, OrganizationId = Library, Borrower = _reader, Version = 1 });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        return host;
    }

    private async Task<bool> ReturnedAsync(ShelfHost host)
    {
        var returned = false;
        await host.InScopeAsync(Library, async database =>
            returned = (await database.Loans.AsNoTracking().SingleAsync(loan => loan.Id == _loan, TestContext.Current.CancellationToken)).Returned);
        return returned;
    }

    private static Task<HttpResponseMessage> PostAsync(ShelfHost host, string path) => SendAsync(host, HttpMethod.Post, path);

    private static async Task<HttpResponseMessage> SendAsync(ShelfHost host, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", ShelfHost.Token(WriteScope));
        request.Headers.Add(BitwardenHeaders.Organization, Library.ToString());
        request.Headers.Add(BitwardenHeaders.Delegation, ShelfHost.SignedContextToken(Library, null));

        return await host.Client().SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A store that holds nothing and fails every invalidation with the exception given.
    /// </summary>
    private sealed class FailingStore(Exception failure) : IOwnedCacheStore
    {
        public bool Enabled => true;

        public Task<IReadOnlyDictionary<string, string>?> ReadAsync(string key) =>
            Task.FromResult<IReadOnlyDictionary<string, string>?>(null);

        public Task<bool> FillAsync(string key, long version, string? owner, string? data, TimeSpan lifetime) =>
            Task.FromResult(false);

        public Task InvalidateAsync(IReadOnlyCollection<Invalidation> invalidations, TimeSpan lifetime, TimeSpan arrivalLifetime) =>
            Task.FromException(failure);
    }
}
