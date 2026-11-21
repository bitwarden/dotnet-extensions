using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Resources.Caching.Tests;

/// <summary>
/// A row the database deletes or changes through a foreign key never passes through the change tracker, so a cached
/// read built from one must say why that leaves no entry stale: undeclared, the host refuses to start in Development and
/// under test, and warns once elsewhere; declared wrongly, it refuses everywhere.
/// </summary>
public sealed class CacheCascadeCheckTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private static readonly Guid Ours = Guid.NewGuid();

    private readonly Guid _branch = Guid.NewGuid();
    private readonly Guid _copy = Guid.NewGuid();
    private ConnectionMultiplexer _redis = null!;

    public async ValueTask InitializeAsync() => _redis = await ConnectionMultiplexer.ConnectAsync(redis.Hostname);

    public async ValueTask DisposeAsync() => await _redis.DisposeAsync();

    private string CopyKey => $"{ShelfHost.ServiceName}:copy:{_copy}";

    // Cascade and SetNull, on the read's own row or on a row it includes.
    [Theory]
    [InlineData("Testing", typeof(GetCopyQuery), "CopyEntity.BranchId (Cascade from BranchEntity)")]
    [InlineData("Development", typeof(GetCopyQuery), "CopyEntity.BranchId (Cascade from BranchEntity)")]
    [InlineData("Testing", typeof(GetNoticeQuery), "NoticeEntity.BranchId (SetNull from BranchEntity)")]
    [InlineData("Testing", typeof(UncoveredShelfQuery), "PlacementEntity.ShelfId (Cascade from ShelfEntity)")]
    public async Task Start_UndeclaredCascade_InDevelopmentOrUnderTest_Refused(string environment, Type query, string expected)
    {
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ShelfHost.StartAsync(null, services => Register(services, query).AddOwnedCache<ShelfContext>(), environment));

        Assert.Contains($"{query.Name} reads {expected}", refusal.Message);
        Assert.Contains("CascadeCovered", refusal.Message);
    }

    // One warning names every undeclared key, of every read.
    [Fact]
    public async Task Start_UndeclaredCascade_Elsewhere_WarnsOnceAndStarts()
    {
        await using var host = await ShelfHost.StartAsync(
            null,
            services => services.AddFakeLogging()
                .AddQuery<IGetCopyQuery, GetCopyQuery>()
                .AddQuery<IGetNoticeQuery, GetNoticeQuery>()
                .AddOwnedCache<ShelfContext>(),
            "Production");

        var warning = Assert.Single(host.Services.GetFakeLogCollector().GetSnapshot(), record => record.Level == LogLevel.Warning);
        Assert.Contains("GetCopyQuery reads CopyEntity.BranchId (Cascade from BranchEntity)", warning.Message);
        Assert.Contains("GetNoticeQuery reads NoticeEntity.BranchId (SetNull from BranchEntity)", warning.Message);
    }

    // A key whose cascade only the change tracker makes needs nothing: the database refuses the delete otherwise.
    // Declared, a cascade starts.
    [Theory]
    [InlineData(typeof(GetStampQuery))]
    [InlineData(typeof(LoadedCopyQuery))]
    [InlineData(typeof(GetShelfQuery))]
    public async Task Start_ClientCascadeOrDeclared_Starts(Type query)
    {
        await using var host = await ShelfHost.StartAsync(null, services => Register(services, query).AddOwnedCache<ShelfContext>());
    }

    // A declaration that isn't true of the model refuses the host wherever it runs.
    [Theory]
    [InlineData(typeof(ParentlessCopyQuery), "not the key an included navigation names CopyEntity by")]
    [InlineData(typeof(StrayCascadeCopyQuery), "CopyEntity.Title, which is no foreign key")]
    public async Task Start_DeclarationUntrueOfTheModel_RefusedEverywhere(Type query, string expected)
    {
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ShelfHost.StartAsync(null, services => Register(services, query).AddOwnedCache<ShelfContext>(), "Production"));

        Assert.Contains(expected, refusal.Message);
    }

    // A cached read-many is built from rows as a read of one is, so it is held to the same declarations.
    [Fact]
    public async Task Start_CachedReadManyWithAnUndeclaredCascade_Refused()
    {
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => ShelfHost.StartAsync(
            null, services => services.AddQuery<IListCopies, OrganizationsCopiesList>().AddOwnedCache<ShelfContext>()));

        Assert.Contains("OrganizationsCopiesList reads CopyEntity.BranchId (Cascade from BranchEntity)", refusal.Message);
    }

    [Fact]
    public async Task Start_CachedReadManyWithItsCascadeDeclared_Starts()
    {
        await using var host = await ShelfHost.StartAsync(
            null, services => services.AddQuery<IListCopies, LoadedOrganizationsCopiesList>().AddOwnedCache<ShelfContext>());
    }

    // A read-many includes nothing, so no parent's entry can cover its rows.
    [Fact]
    public async Task Start_CachedReadManyDeclaringItsCascadeParentInvalidated_RefusedEverywhere()
    {
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => ShelfHost.StartAsync(
            null,
            services => services.AddQuery<IListCopies, ParentlessOrganizationsCopiesList>().AddOwnedCache<ShelfContext>(),
            "Production"));

        Assert.Contains("not the key an included navigation names CopyEntity by", refusal.Message);
    }

    [Fact]
    public void CascadeCovered_OnACachedReadMany_Kept() =>
        Assert.Equal(
            ["BranchId"],
            Assert.Single(new CacheableReadManyBuilder<CopyEntity, Copy>()
                .CascadeCovered<CopyEntity>(copy => copy.BranchId, CascadeCoverage.DependentsLoaded, "loaded").CascadeDeclarations).Properties);

    [Fact]
    public void CascadeCovered_ByAnExpressionThatIsNoProperty_Refused() =>
        Assert.Throws<ArgumentException>(() => new CacheableQueryBuilder<CopyEntity, Copy>()
            .CascadeCovered<CopyEntity>(copy => copy.Title.Length, CascadeCoverage.DependentsLoaded, "no property"));

    // A key of several properties is declared by them all, in the key's order.
    [Fact]
    public void CascadeCovered_SeveralProperties_KeptInOrder()
    {
        var builder = new CacheableQueryBuilder<CopyEntity, Copy>()
            .CascadeCovered<CopyEntity>(copy => new { copy.OrganizationId, copy.BranchId }, CascadeCoverage.DependentsLoaded, "both");

        Assert.Equal(["OrganizationId", "BranchId"], Assert.Single(builder.CascadeDeclarations).Properties);
    }

    [Fact]
    public void CascadeCovered_WithoutAReason_Refused() =>
        Assert.Throws<ArgumentException>(() => new CacheableQueryBuilder<CopyEntity, Copy>()
            .CascadeCovered<CopyEntity>(copy => copy.BranchId, CascadeCoverage.DependentsLoaded, " "));

    // What the declaration promises: deleting the branch with its copies loaded invalidates their entries, and without,
    // the database deletes a copy whose entry is still read.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeleteBranch_InvalidatesCopiesOnlyWhenLoaded(bool loaded)
    {
        await using var host = await ShelfHost.StartAsync(
            redis.Service, services => services.AddQuery<IGetCopyQuery, LoadedCopyQuery>().AddOwnedCache<ShelfContext>());
        await host.InScopeAsync(null, async database =>
        {
            database.Branches.Add(new BranchEntity { Id = _branch, Name = "Arrakeen" });
            database.Copies.Add(new CopyEntity { Id = _copy, OrganizationId = Ours, BranchId = _branch, Title = "Dune", Version = 1 });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });
        await host.InServiceScopeAsync(Ours, async services =>
            await services.GetRequiredService<IGetCopyQuery>().ReadAsync(_copy.ToString(), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Dune", (await Entry())[OwnedCacheEntry.Data]);

        await host.InScopeAsync(Ours, async database =>
        {
            if (loaded)
            {
                await database.Copies.Where(copy => copy.BranchId == _branch).LoadAsync(TestContext.Current.CancellationToken);
            }

            database.Branches.Remove(await database.Branches.SingleAsync(branch => branch.Id == _branch, TestContext.Current.CancellationToken));
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
            Assert.False(await database.Copies.IgnoreQueryFilters().AnyAsync(copy => copy.Id == _copy, TestContext.Current.CancellationToken));
        });

        var entry = await Entry();
        Assert.Equal(loaded, entry.ContainsKey(OwnedCacheEntry.Gone));
        Assert.Equal(!loaded, entry.ContainsKey(OwnedCacheEntry.Data));
    }

    // Why a ClientCascade key needs no declaration: the database refuses to delete the principal while a row the
    // change tracker didn't load still depends on it, so nothing is deleted behind the cache's back.
    [Fact]
    public async Task DeleteBranch_WithAStampNotLoaded_RefusedByTheDatabase()
    {
        await using var host = await ShelfHost.StartAsync(
            null, services => services.AddQuery<IGetStampQuery, GetStampQuery>().AddOwnedCache<ShelfContext>());
        var stamp = Guid.NewGuid();
        await host.InScopeAsync(null, async database =>
        {
            database.Branches.Add(new BranchEntity { Id = _branch, Name = "Arrakeen" });
            database.Stamps.Add(new StampEntity { Id = stamp, OrganizationId = Ours, BranchId = _branch, Version = 1 });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => host.InScopeAsync(Ours, async database =>
        {
            database.Branches.Remove(await database.Branches.SingleAsync(branch => branch.Id == _branch, TestContext.Current.CancellationToken));
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        }));

        await host.InScopeAsync(null, async database =>
            Assert.True(await database.Stamps.IgnoreQueryFilters().AnyAsync(row => row.Id == stamp, TestContext.Current.CancellationToken)));
    }

    private static IServiceCollection Register(IServiceCollection services, Type query)
    {
        var contract = query.GetInterfaces().Single(type => type.Name.StartsWith("IGet", StringComparison.Ordinal));
        services.AddScoped(contract, query);
        return services;
    }

    private async Task<Dictionary<string, string>> Entry() =>
        (await _redis.GetDatabase().HashGetAllAsync(CopyKey)).ToDictionary(field => field.Name.ToString(), field => field.Value.ToString());
}
