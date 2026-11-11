using Bitwarden.Server.Sdk.Idempotency;
using Microsoft.Extensions.DependencyInjection;
using static Bitwarden.Server.Sdk.Client.Tests.PipelineHarness;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// A write to an operation the callee's contract marks as accepting keys carries one, the same on every attempt; any
/// other write carries none.
/// </summary>
public sealed class IdempotencyKeyHandlerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task AWrite_CarriesAFreshKey(string method)
    {
        var callee = new Scripted(Ok);
        await using var services = Services(callee);
        var probe = services.GetRequiredService<Probe>();

        using var first = await probe.SendAsync(new HttpMethod(method), Token);
        using var second = await probe.SendAsync(new HttpMethod(method), Token);

        var keys = callee.Attempts.Select(attempt => attempt.Key).ToList();
        Assert.All(keys, key => Assert.True(Guid.TryParse(key, out _)));
        Assert.NotEqual(keys[0], keys[1]);
    }

    // The test contract (openapi.json, embedded as a client package embeds its service's) leaves settings/{id} unmarked.
    [Fact]
    public async Task AWriteToAnOperationTheContractDoesNotMark_CarriesNoKey()
    {
        var callee = new Scripted(Ok);
        await using var services = Services(callee);

        using var response = await services.GetRequiredService<Probe>().SendAsync(HttpMethod.Put, Token, address: "settings/1");

        Assert.Null(Assert.Single(callee.Attempts).Key);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task ARead_CarriesNoKey(string method)
    {
        var callee = new Scripted(Ok);
        await using var services = Services(callee);

        using var response = await services.GetRequiredService<Probe>().SendAsync(new HttpMethod(method), Token);

        Assert.Null(Assert.Single(callee.Attempts).Key);
    }

    [Fact]
    public async Task AKeyTheCallSiteSet_IsKept()
    {
        var callee = new Scripted(Ok);
        await using var services = Services(callee);

        using var response = await services.GetRequiredService<Probe>().SendAsync(HttpMethod.Post, Token, key: "set-by-the-call-site");

        Assert.Equal("set-by-the-call-site", Assert.Single(callee.Attempts).Key);
    }

    // An operation run again from the start sends the same keys in the same order; two identical writes to one address
    // within it get different keys.
    [Fact]
    public async Task AnOperation_RunAgain_SendsTheSameKeys()
    {
        var callee = new Scripted(Ok);
        await using var services = Services(callee);
        var probe = services.GetRequiredService<Probe>();

        async Task<List<string?>> RunAsync()
        {
            using (IdempotentOperation.Begin("operation-1"))
            {
                Assert.Equal("operation-1", IdempotentOperation.Current);
                using var one = await probe.SendAsync(HttpMethod.Post, Token);
                using var two = await probe.SendAsync(HttpMethod.Post, Token);
                using var three = await probe.SendAsync(HttpMethod.Delete, Token);
            }

            Assert.Null(IdempotentOperation.Current);
            var keys = callee.Attempts.Select(attempt => attempt.Key).ToList();
            callee.Attempts.Clear();
            return keys;
        }

        var first = await RunAsync();
        var again = await RunAsync();

        Assert.Equal(first, again);
        Assert.Equal(3, first.Distinct().Count());
    }

    [Fact]
    public async Task AnotherOperation_SendsOtherKeys()
    {
        var callee = new Scripted(Ok);
        await using var services = Services(callee);
        var probe = services.GetRequiredService<Probe>();

        using (IdempotentOperation.Begin("operation-1"))
        {
            using var one = await probe.SendAsync(HttpMethod.Post, Token);
        }

        using (IdempotentOperation.Begin("operation-2"))
        {
            using var two = await probe.SendAsync(HttpMethod.Post, Token);
        }

        Assert.NotEqual(callee.Attempts.First().Key, callee.Attempts.Last().Key);
    }

    // A run that makes its writes to one address in another order (it read its work in another order) still sends each
    // write the key it had, because the body is part of the key; and a write with another body gets another key.
    [Fact]
    public async Task AnOperation_RunAgainInAnotherOrder_SendsEachWriteItsKey()
    {
        var callee = new Scripted(Ok);
        await using var services = Services(callee);
        var probe = services.GetRequiredService<Probe>();

        async Task<Dictionary<string, string?>> RunAsync(params string[] notes)
        {
            using (IdempotentOperation.Begin("operation-1"))
            {
                foreach (var note in notes)
                {
                    using var response = await probe.SendAsync(HttpMethod.Post, Token, body: new { note });
                }
            }

            var keys = callee.Attempts.ToDictionary(attempt => attempt.Body!, attempt => attempt.Key);
            callee.Attempts.Clear();
            return keys;
        }

        var first = await RunAsync("a", "b");
        var again = await RunAsync("b", "a", "c");

        Assert.Equal(first["""{"note":"a"}"""], again["""{"note":"a"}"""]);
        Assert.Equal(first["""{"note":"b"}"""], again["""{"note":"b"}"""]);
        Assert.DoesNotContain(again["""{"note":"c"}"""], first.Values);
    }
}
