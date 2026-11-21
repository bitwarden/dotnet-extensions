using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Resources.Caching.Tests;

/// <summary>
/// A cached read that includes rows of another table: a change to one of them moves its parent's version on, so the
/// parent's entry is invalidated with the version it replaced as the dead one.
/// </summary>
public sealed class IncludedRowsTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private static readonly Guid Ours = Guid.NewGuid();

    private readonly Guid _shelf = Guid.NewGuid();
    private readonly Guid _placement = Guid.NewGuid();
    private ConnectionMultiplexer _redis = null!;

    private IDatabase Redis => _redis.GetDatabase();

    private string ShelfKey => $"{ShelfHost.ServiceName}:shelf:{_shelf}";

    public async ValueTask InitializeAsync() => _redis = await ConnectionMultiplexer.ConnectAsync(redis.Hostname);

    public async ValueTask DisposeAsync() => await _redis.DisposeAsync();

    public static TheoryData<string> Changes => ["added", "modified", "deleted"];

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task Save_AnIncludedRow_MovesItsParentsVersionAndInvalidatesIt(string change)
    {
        await using var host = await StartAsync();
        (await host.GetAsync($"/api/v1/shelves/{_shelf}", Ours)).Dispose();
        Assert.Equal("5", (string?)await Redis.HashGetAsync(ShelfKey, OwnedCacheEntry.Version));

        await host.InScopeAsync(Ours, async database =>
        {
            switch (change)
            {
                case "added":
                    database.Placements.Add(new PlacementEntity { Id = Guid.NewGuid(), ShelfId = _shelf, OrganizationId = Ours, Title = "Children of Dune" });
                    break;
                case "modified":
                    (await database.Placements.SingleAsync(row => row.Id == _placement, TestContext.Current.CancellationToken)).Title = "Dune Messiah";
                    break;
                default:
                    database.Placements.Remove(await database.Placements.SingleAsync(row => row.Id == _placement, TestContext.Current.CancellationToken));
                    break;
            }

            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        // 5 is dead, and the shelf committed 6.
        Assert.Equal("1", (string?)await Redis.HashGetAsync(ShelfKey, OwnedCacheEntry.Gone));
        Assert.Equal("5", (string?)await Redis.HashGetAsync(ShelfKey, OwnedCacheEntry.Version));
        Assert.Equal(6, await VersionAsync(host));

        using var response = await host.GetAsync($"/api/v1/shelves/{_shelf}", Ours);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("6", (string?)await Redis.HashGetAsync(ShelfKey, OwnedCacheEntry.Version));
        Assert.False(await Redis.HashExistsAsync(ShelfKey, OwnedCacheEntry.Gone));
    }

    // The parent loaded and changed in the same save: its own change and its row's each move it, and the context keeps
    // the version the database holds, so its next save isn't refused for a version it moved itself.
    [Fact]
    public async Task Save_TheParentAndAnIncludedRowTogether_ContextKeepsTheStoredVersion()
    {
        await using var host = await StartAsync();

        await host.InScopeAsync(Ours, async database =>
        {
            var shelf = await database.Shelves.Include(row => row.Placements).SingleAsync(row => row.Id == _shelf, TestContext.Current.CancellationToken);
            shelf.Name = "Fiction";
            shelf.Placements.Single().Title = "Heretics of Dune";
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);

            shelf.Name = "Science fiction";
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        Assert.Equal(8, await VersionAsync(host));
    }

    // An include of an include: a change to its rows can't name the cached row, so the read can't be cached as declared.
    [Fact]
    public async Task Start_AnIncludeTheCacheCantInvalidate_Refused()
    {
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => ShelfHost.StartAsync(redis.Service, services =>
            services.AddQuery<IGetShelfQuery, DeepShelfQuery>().AddOwnedCache<ShelfContext>()));

        Assert.Contains("which the service cache can't invalidate", refusal.Message);
    }

    private async Task<ShelfHost> StartAsync()
    {
        var host = await ShelfHost.StartAsync(redis.Service, services =>
            services.AddQuery<IGetShelfQuery, GetShelfQuery>().AddOwnedCache<ShelfContext>());

        await host.InScopeAsync(Ours, async database =>
        {
            database.Shelves.Add(new ShelfEntity { Id = _shelf, OrganizationId = Ours, Name = "Dune", Version = 4 });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
            database.Placements.Add(new PlacementEntity { Id = _placement, ShelfId = _shelf, OrganizationId = Ours, Title = "Dune" });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        // The placement's own write moved the shelf on, and invalidated it: each test starts from 5, unread.
        Assert.Equal(5, await VersionAsync(host));
        await Redis.KeyDeleteAsync(ShelfKey);

        return host;
    }

    private async Task<int> VersionAsync(ShelfHost host)
    {
        var version = 0;
        await host.InScopeAsync(Ours, async database =>
            version = (await database.Shelves.AsNoTracking().SingleAsync(row => row.Id == _shelf, TestContext.Current.CancellationToken)).Version);
        return version;
    }
}
