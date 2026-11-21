using System.Net;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// Each inbound request has a memo of its own: a repeated read of another service is sent once, a write in between sends
/// it again, an operation marked <see cref="WithoutRequestMemoAttribute"/> has none, and the switch turns it off.
/// </summary>
public sealed class RequestMemoScopeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Request_ReadsTwice_SentOnce()
    {
        await using var service = await StartAsync();

        await GetAsync(service, "/memo/twice");

        Assert.Single(service.Recorded.Downstream);
    }

    [Fact]
    public async Task Requests_EachReadingOnce_EachSent()
    {
        await using var service = await StartAsync();

        await GetAsync(service, "/memo/twice");
        await GetAsync(service, "/memo/twice");

        Assert.Equal(2, service.Recorded.Downstream.Count);
    }

    [Fact]
    public async Task Request_ReadsWritesReads_ReadSentAgain()
    {
        await using var service = await StartAsync();

        await GetAsync(service, "/memo/read-write-read");

        Assert.Equal(3, service.Recorded.Downstream.Count);
    }

    [Fact]
    public async Task Request_Fresh_SentAgain()
    {
        await using var service = await StartAsync();

        await GetAsync(service, "/memo/fresh");

        Assert.Equal(2, service.Recorded.Downstream.Count);
    }

    [Fact]
    public async Task Request_ParallelReads_SentOnce()
    {
        await using var service = await StartAsync();

        await GetAsync(service, "/memo/parallel");

        Assert.Single(service.Recorded.Downstream);
    }

    [Fact]
    public async Task Request_OperationWithoutMemo_SentEachTime()
    {
        await using var service = await StartAsync();

        await GetAsync(service, "/memo/declined");

        Assert.Equal(2, service.Recorded.Downstream.Count);
    }

    [Fact]
    public async Task Request_OperationWithoutMemoInItsMetadata_SentEachTime()
    {
        await using var service = await StartAsync();

        await GetAsync(service, "/memo/declined-by-metadata");

        Assert.Equal(2, service.Recorded.Downstream.Count);
    }

    [Fact]
    public async Task Request_SwitchedOff_SentEachTime()
    {
        await using var service = await StartAsync(new Dictionary<string, string?> { [RequestMemo.EnabledKey] = "false" });

        await GetAsync(service, "/memo/twice");

        Assert.Equal(2, service.Recorded.Downstream.Count);
    }

    [Fact]
    public async Task Request_PastTheConfiguredEntries_SentEachTime()
    {
        await using var service = await StartAsync(new Dictionary<string, string?> { [RequestMemo.MaxEntriesKey] = "0" });

        await GetAsync(service, "/memo/twice");

        Assert.Equal(2, service.Recorded.Downstream.Count);
    }

    private static Task<SessionHost> StartAsync(IReadOnlyDictionary<string, string?>? configuration = null) =>
        SessionHost.StartAsync(productService: false, configuration, endpoints: Map);

    private static async Task GetAsync(SessionHost service, string path)
    {
        using var response = await service.Client().GetAsync(path, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static void Map(IEndpointRouteBuilder endpoints)
    {
        var memo = endpoints.MapGroup("/memo").AllowAnonymous().ExcludeFromDescription();
        memo.MapGet("twice", Twice);
        memo.MapGet("read-write-read", ReadWriteRead);
        memo.MapGet("fresh", Fresh);
        memo.MapGet("parallel", Parallel);
        memo.MapGet("declined", Declined);
        memo.MapGet("declined-by-metadata", Twice).WithMetadata(new WithoutRequestMemoAttribute());
    }

    private static async Task<IResult> Twice(DownstreamClient downstream, CancellationToken cancellationToken)
    {
        await downstream.CallAsync(cancellationToken);
        await downstream.CallAsync(cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> ReadWriteRead(DownstreamClient downstream, CancellationToken cancellationToken)
    {
        await downstream.CallAsync(cancellationToken);
        await downstream.WriteAsync("api/v1/things", cancellationToken);
        await downstream.CallAsync(cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Fresh(DownstreamClient downstream, CancellationToken cancellationToken)
    {
        await downstream.CallAsync(cancellationToken);
        using (Client.OutgoingCall.Fresh())
        {
            await downstream.CallAsync(cancellationToken);
        }

        await downstream.CallAsync(cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Parallel(DownstreamClient downstream, CancellationToken cancellationToken)
    {
        await Task.WhenAll(downstream.CallAsync(cancellationToken), downstream.CallAsync(cancellationToken));
        return Results.Ok();
    }

    [WithoutRequestMemo]
    private static async Task<IResult> Declined(DownstreamClient downstream, CancellationToken cancellationToken)
    {
        await downstream.CallAsync(cancellationToken);
        await downstream.CallAsync(cancellationToken);
        return Results.Ok();
    }
}
