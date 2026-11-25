using Bitwarden.Server.Sdk.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Resources.Caching.Tests;

/// <summary>
/// A write in bulk passes no row through the change tracker, so on a table a cache entry is built from it is refused;
/// an owner's erasure, which tells the cache itself, is let through.
/// </summary>
public sealed class CacheBulkWriteGuardTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private static readonly Guid Ours = Guid.NewGuid();

    private readonly Guid _book = Guid.NewGuid();
    private readonly Guid _shelf = Guid.NewGuid();
    private ConnectionMultiplexer _redis = null!;

    public async ValueTask InitializeAsync() => _redis = await ConnectionMultiplexer.ConnectAsync(redis.Hostname);

    public async ValueTask DisposeAsync() => await _redis.DisposeAsync();

    [Theory]
    [InlineData(null)]
    [InlineData("caching off")]
    public async Task ExecuteUpdate_OnAResourcesTable_Refused(string? off)
    {
        await using var host = await StartAsync(off is null ? redis.Service : null);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => host.InScopeAsync(Ours, async database =>
            await database.Books.Where(row => row.Id == _book)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Title, "Dune Messiah"), TestContext.Current.CancellationToken)));

        Assert.Contains("ExecuteUpdate on BookEntity is refused", refusal.Message);
        await host.InScopeAsync(Ours, async database =>
            Assert.Equal("Dune", (await database.Books.SingleAsync(row => row.Id == _book, TestContext.Current.CancellationToken)).Title));
    }

    [Fact]
    public async Task ExecuteDelete_OnATableAReadIncludes_Refused()
    {
        await using var host = await StartAsync(redis.Service);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => host.InScopeAsync(Ours, async database =>
            await database.Placements.Where(row => row.ShelfId == _shelf).ExecuteDeleteAsync(TestContext.Current.CancellationToken)));

        Assert.Contains("ExecuteDelete on PlacementEntity is refused", refusal.Message);
    }

    // The erasure's tag, borrowed outside an erasure, lets nothing through.
    [Fact]
    public async Task ExecuteDelete_TaggedAsAnErasureOutsideOne_Refused()
    {
        await using var host = await StartAsync(redis.Service);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => host.InScopeAsync(Ours, async database =>
            await database.Books.TagWith(OwnerErasure.Tag).Where(row => row.Id == _book).ExecuteDeleteAsync(TestContext.Current.CancellationToken)));

        Assert.Contains("outside an owner's erasure", refusal.Message);
    }

    [Fact]
    public async Task ExecuteDelete_OnATableNothingCachedIsBuiltFrom_Allowed()
    {
        await using var host = await StartAsync(redis.Service);

        var borrower = Guid.NewGuid();

        await host.InScopeAsync(null, async database =>
            await database.Suspensions.Where(row => row.Borrower == borrower).ExecuteDeleteAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Erasure_OfCachedTables_AllowedAndInvalidated()
    {
        await using var host = await StartAsync(redis.Service);
        (await host.GetAsync($"/api/v1/shelves/{_shelf}", Ours)).Dispose();

        await host.InScopeAsync(null, async database =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await OwnerErasure.EraseAsync(database, Owner.Organization, Ours, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        });

        await host.InScopeAsync(null, async database =>
        {
            Assert.False(await database.Books.IgnoreQueryFilters().AnyAsync(row => row.Id == _book, TestContext.Current.CancellationToken));
            Assert.False(await database.Placements.IgnoreQueryFilters().AnyAsync(row => row.ShelfId == _shelf, TestContext.Current.CancellationToken));
        });
        Assert.Equal("1", (string?)await _redis.GetDatabase().HashGetAsync($"{ShelfHost.ServiceName}:shelf:{_shelf}", OwnedCacheEntry.Gone));
        Assert.False(OwnerErasure.Erasing);
    }

    private async Task<ShelfHost> StartAsync(string? cache)
    {
        var host = await ShelfHost.StartAsync(cache, services =>
            services
                .AddQuery<IGetBookQuery, GetBookQuery>()
                .AddQuery<IGetShelfQuery, GetShelfQuery>()
                .AddOwnedCache<ShelfContext>());

        await host.InScopeAsync(Ours, async database =>
        {
            database.Books.Add(new BookEntity { Id = _book, OrganizationId = Ours, Title = "Dune", Version = 1 });
            database.Shelves.Add(new ShelfEntity { Id = _shelf, OrganizationId = Ours, Name = "Dune", Version = 1 });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
            database.Placements.Add(new PlacementEntity { Id = Guid.NewGuid(), ShelfId = _shelf, OrganizationId = Ours, Title = "Dune" });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        return host;
    }
}
