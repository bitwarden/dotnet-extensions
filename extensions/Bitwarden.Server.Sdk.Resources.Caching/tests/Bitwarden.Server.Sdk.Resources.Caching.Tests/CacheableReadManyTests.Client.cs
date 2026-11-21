using System.Text.Json;
using Bitwarden.Server.Sdk.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Bitwarden.Server.Sdk.Resources.Caching.Tests;

/// <summary>
/// A cached read-many read by a client of the cache, from the contract its owner publishes. Not compiled into the
/// generated tests, which can't see the client's internals.
/// </summary>
public sealed partial class CacheableReadManyTests
{
    // A client of the cache answers a page of the read-many, of the person its call carries, sliced as the service
    // pages it, and nothing else.
    [Fact]
    public async Task ClientFromThePublishedContract_ReadsAPageOfTheReadManyAndOnlyIt()
    {
        await using var host = await StartAsync();
        var fromService = await PageBodyAsync(host, $"{Path}?fields[open-loan]=title&page[number]=2&page[size]=1");
        using var response = await host.Client().GetAsync("/api/openapi.json", Ct);
        var contract = Client.CacheableRead.FromContract(await response.Content.ReadAsStreamAsync(Ct));
        await using var connection = new Client.CachedReadsConnection(redis.Hostname, new FakeTimeProvider());
        var reads = Reads(contract, new StatedContext { UserId = _borrower }, connection);

        var answer = await reads.ReadManyAsync("listOpenLoans", 2, 1, ["title"], Ct);

        Assert.Equal(Client.CachedAnswerKind.Hit, answer.Kind);
        Assert.Equal(2, answer.Total);
        using (var page = JsonDocument.Parse(answer.Resource!))
        using (var served = JsonDocument.Parse(fromService))
        {
            Assert.Equal(served.RootElement.GetProperty("data").GetRawText(), page.RootElement.GetRawText());
            Assert.Equal([_second.ToString()], page.RootElement.EnumerateArray().Select(loan => loan.GetProperty("id").GetString()));
            Assert.False(page.RootElement[0].GetProperty("attributes").TryGetProperty("organization", out _));
        }

        // Past the last page: empty, with the whole count.
        var past = await reads.ReadManyAsync("listOpenLoans", 3, 1, [], Ct);
        Assert.Equal("[]", past.Resource);
        Assert.Equal(2, past.Total);

        // A read of one resource, or a call carrying an organization: a miss.
        Assert.Equal(Client.CachedAnswerKind.Miss, (await reads.ReadCurrentAsync("listOpenLoans", ["title"], Ct)).Kind);
        var inOrganization = Reads(contract, new StatedContext { UserId = _borrower, OrganizationId = _library }, connection);
        Assert.Equal(Client.CachedAnswerKind.Miss, (await inOrganization.ReadManyAsync("listOpenLoans", 1, 10, ["title"], Ct)).Kind);

        // Another person's call reads their own key, never this one: tampered to name this one's entry, its owner refuses.
        var other = Reads(contract, new StatedContext { UserId = _other }, connection);
        await Redis.KeyCopyAsync(ListKey(_borrower), ListKey(_other));
        Assert.Equal(Client.CachedAnswerKind.Miss, (await other.ReadManyAsync("listOpenLoans", 1, 10, ["title"], Ct)).Kind);
    }

    private static Client.CachedReads<CacheableReadManyTests> Reads(
        IReadOnlyDictionary<string, Client.CacheableRead> contract,
        StatedContext context,
        Client.CachedReadsConnection connection)
    {
        var services = new ServiceCollection()
            .AddSingleton<IContext>(context)
            .Configure<Client.BitwardenClientOptions>(ShelfHost.ServiceName, options =>
            {
                options.Scopes = [ShelfHost.ReadScope];

                // Asserting the person's context on its own authority, as the operation admits from this caller.
                options.CallerName = "some-service";
            })
            .BuildServiceProvider();

        return new Client.CachedReads<CacheableReadManyTests>(
            ShelfHost.ServiceName,
            contract,
            connection,
            services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Client.BitwardenClientOptions>>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Client.CachedReads<CacheableReadManyTests>>.Instance,
            new Client.RequestMemoMetrics(new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()));
    }
}
