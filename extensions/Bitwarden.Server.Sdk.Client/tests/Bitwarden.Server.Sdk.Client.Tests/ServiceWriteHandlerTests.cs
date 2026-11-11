using System.Net;
using Bitwarden.Server.Sdk.Sagas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Bitwarden.Server.Sdk.Client.Tests.PipelineHarness;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// Each write a client sends is reported, once, to the operation running now.
/// </summary>
public sealed class ServiceWriteHandlerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // Each write is reported before it is sent, once however often it is retried: a span is noticed before the write
    // that makes it, which a refusal stops.
    [Fact]
    public async Task AWriteRetried_IsCountedOnce_AndARefusalStopsTheSecond()
    {
        var callee = new Scripted(StillRunning, Ok, Ok);
        await using var services = Services(callee);
        var probe = services.GetRequiredService<Probe>();
        var guard = new RefusingGuard();

        using var operation = ServiceWrites.Begin("operation", declared: null, guard);
        using (var first = await probe.SendAsync(HttpMethod.Post, Token))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        Assert.Equal([Callee], operation.Steps);

        await Assert.ThrowsAsync<InvalidOperationException>(() => probe.SendAsync(HttpMethod.Delete, Token));

        Assert.Equal(2, callee.Attempts.Count);
        Assert.Equal([Callee, Callee], Assert.Single(guard.Told));
    }

    // Reads, and writes whose address says they only read, are not writes.
    [Theory]
    [InlineData("GET", "things")]
    [InlineData("HEAD", "things")]
    [InlineData("POST", "things/presented/actions/check-plan")]
    [InlineData("POST", "things?validateOnly=true")]
    [InlineData("POST", "invoice-previews")]
    public async Task ARead_IsNotCounted(string method, string address)
    {
        var callee = new Scripted(Ok);
        await using var services = Services(callee);

        using var operation = ServiceWrites.Begin("operation", declared: null, guard: null);
        using var response = await services.GetRequiredService<Probe>().SendAsync(new HttpMethod(method), Token, address: address);

        Assert.Empty(operation.Steps);
    }

    // A write-method operation the client declares reading (ReadingPaths) is not a write; one beside it is.
    [Theory]
    [InlineData("users/1/actions/verify-password", false)]
    [InlineData("users/1/actions/verify-password?x=1", false)]
    [InlineData("users/1/actions/confirm", true)]
    [InlineData("accounts/1/actions/verify-password/extra", true)]
    public async Task AReadingPathTheClientDeclares_IsNotCounted(string address, bool counted)
    {
        var callee = new Scripted(Ok);
        await using var services = Services(callee, configure: options => options.ReadingPaths.Add("users/*/actions/verify-password"));

        using var operation = ServiceWrites.Begin("operation", declared: null, guard: null);
        using var response = await services.GetRequiredService<Probe>().SendAsync(HttpMethod.Post, Token, address: address);

        Assert.Equal(counted ? 1 : 0, operation.Steps.Count);
    }

    private sealed class RefusingGuard : IServiceWritesGuard
    {
        public List<IReadOnlyList<string>> Told { get; } = [];

        public void Spanned(string operation, IReadOnlyList<string> steps, SpanDeclarationAttribute? declared)
        {
            Told.Add(steps);
            throw new InvalidOperationException("Refused.");
        }
    }
}
