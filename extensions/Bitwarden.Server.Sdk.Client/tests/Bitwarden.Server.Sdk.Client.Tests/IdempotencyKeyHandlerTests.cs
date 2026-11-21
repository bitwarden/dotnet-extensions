using System.Net;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Idempotency;
using Microsoft.Extensions.Configuration;
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
            using (IdempotentOperation.Begin("operation-1", Secret))
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

        using (IdempotentOperation.Begin("operation-1", Secret))
        {
            using var one = await probe.SendAsync(HttpMethod.Post, Token);
        }

        using (IdempotentOperation.Begin("operation-2", Secret))
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
            using (IdempotentOperation.Begin("operation-1", Secret))
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

    // Inside an operation a write's key is derived from whom it concerns, as the call carries it: two writes alike but
    // for whom keep their own keys whatever order a run makes them in, and each is the key the operation derives from the
    // headers the callee received.
    [Fact]
    public async Task AnOperation_KeysEachWriteByWhomItConcerns()
    {
        var callee = new Scripted(Ok);
        await using var services = Services(callee);
        var probe = services.GetRequiredService<Probe>();
        var ann = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var organization = Guid.NewGuid();

        async Task<Dictionary<string, string?>> RunAsync(params Guid[] users)
        {
            using (IdempotentOperation.Begin("operation-1", Secret))
            {
                foreach (var user in users)
                {
                    using (OutgoingCall.About(user, organization))
                    {
                        using var response = await probe.SendAsync(HttpMethod.Post, Token, body: new { note = "same" });
                    }
                }
            }

            var keys = callee.Attempts.ToDictionary(attempt => attempt.User!, attempt => attempt.Key);
            Assert.All(callee.Attempts, attempt => Assert.Equal(organization.ToString(), attempt.Organization));
            callee.Attempts.Clear();
            return keys;
        }

        var first = await RunAsync(ann, bob);
        var again = await RunAsync(bob, ann);

        Assert.Equal(first, again);
        Assert.NotEqual(first[ann.ToString()], first[bob.ToString()]);

        using (IdempotentOperation.Begin("operation-1", Secret))
        {
            Assert.Equal(
                first[ann.ToString()],
                IdempotentOperation.KeyForWrite(
                    "POST",
                    "http://callee.invalid/things",
                    """{"note":"same"}"""u8,
                    [new(BitwardenHeaders.Organization, organization.ToString()), new(BitwardenHeaders.User, ann.ToString())]));
        }
    }

    // Every attempt of a write carries the headers its key was derived from, worked out once for the call.
    [Fact]
    public async Task AnOperation_EveryAttemptCarriesWhomItsKeyNames()
    {
        var callee = new Scripted(StillRunning, Ok);
        await using var services = Services(callee);
        var user = Guid.NewGuid();

        using (IdempotentOperation.Begin("operation-1", Secret))
        using (OutgoingCall.AboutUser(user))
        {
            using var response = await services.GetRequiredService<Probe>().SendAsync(HttpMethod.Post, Token);
        }

        Assert.Equal(2, callee.Attempts.Count);
        Assert.Single(callee.Attempts.Select(attempt => (attempt.Key, attempt.User)).Distinct());
        Assert.Equal(user.ToString(), callee.Attempts.First().User);
    }

    // A write retried after a 503 from in front of the callee is the same write: every attempt carries the same key and
    // the same headers, including the person a statement named, which the call took once.
    [Fact]
    public async Task AnOperation_AWriteRetriedAfterA503_SendsTheSameKeyAndHeaders()
    {
        var callee = new Scripted(BareStatus(HttpStatusCode.ServiceUnavailable), Ok);
        var user = Guid.NewGuid();
        await using var services = Services(
            callee,
            register: registered => registered.AddSingleton<IContext>(new StatedContext { UserId = Guid.NewGuid() }));

        using (IdempotentOperation.Begin("operation-1", Secret))
        using (OutgoingCall.AboutUser(user))
        {
            using var response = await services.GetRequiredService<Probe>().SendAsync(HttpMethod.Post, Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(2, callee.Attempts.Count);
        Assert.Single(callee.Attempts.Distinct());
        Assert.Equal(user.ToString(), callee.Attempts.First().User);
        Assert.NotNull(callee.Attempts.First().Key);
    }

    // Each way a write comes to concern somebody: the context's provider, the organization or provider an operation names
    // in its request (IRequestNamedOwner), and the system statements, which carry only what they name. The key is the
    // one derived from exactly the headers every attempt carries.
    [Theory]
    [InlineData("context-provider")]
    [InlineData("request-named-owner")]
    [InlineData("as-system")]
    [InlineData("as-system-about")]
    [InlineData("as-system-for-provider")]
    public async Task AnOperation_KeysAWriteByEveryHeaderItCarries(string concern)
    {
        var callee = new Scripted(BareStatus(HttpStatusCode.ServiceUnavailable), Ok);
        var user = Guid.NewGuid();
        var organization = Guid.NewGuid();
        var provider = Guid.NewGuid();

        IContext context = concern switch
        {
            "context-provider" => new StatedContext { UserId = user, ProviderId = provider },
            "request-named-owner" => new StatedContext { UserId = user },
            _ => new StatedContext { UserId = Guid.NewGuid(), ProviderId = Guid.NewGuid() },
        };
        List<KeyValuePair<string, string>> expected = concern switch
        {
            "context-provider" => [new(BitwardenHeaders.User, user.ToString()), new(BitwardenHeaders.Provider, provider.ToString())],
            "request-named-owner" =>
            [
                new(BitwardenHeaders.Organization, organization.ToString()),
                new(BitwardenHeaders.User, user.ToString()),
                new(BitwardenHeaders.Provider, provider.ToString()),
            ],
            "as-system" => [new(BitwardenHeaders.Organization, organization.ToString())],
            "as-system-about" => [new(BitwardenHeaders.User, user.ToString())],
            "as-system-for-provider" => [new(BitwardenHeaders.Provider, provider.ToString())],
            _ => throw new ArgumentOutOfRangeException(nameof(concern)),
        };

        await using var services = Services(callee, register: registered =>
        {
            registered.AddSingleton(context);
            if (concern == "request-named-owner")
            {
                registered.AddSingleton<IRequestNamedOwner>(new ContextHandlerHarness.Named(organization, provider));
            }
        });

        using (IdempotentOperation.Begin("operation-1", Secret))
        {
            using (concern switch
            {
                "as-system" => OutgoingCall.AsSystem(organization),
                "as-system-about" => OutgoingCall.AsSystemAbout(user),
                "as-system-for-provider" => OutgoingCall.AsSystemForProvider(provider),
                _ => null,
            })
            {
                using var response = await services.GetRequiredService<Probe>()
                    .SendAsync(HttpMethod.Post, Token, body: new { note = "same" });
            }

            var header = (string name) => expected.SingleOrDefault(pair => pair.Key == name).Value;
            Assert.Equal(2, callee.Attempts.Count);
            Assert.All(callee.Attempts, attempt =>
            {
                Assert.Equal(header(BitwardenHeaders.Organization), attempt.Organization);
                Assert.Equal(header(BitwardenHeaders.User), attempt.User);
                Assert.Equal(header(BitwardenHeaders.Provider), attempt.Provider);
            });
        }

        using (IdempotentOperation.Begin("operation-1", Secret))
        {
            var key = IdempotentOperation.KeyForWrite("POST", "http://callee.invalid/things", """{"note":"same"}"""u8, expected);
            Assert.All(callee.Attempts, attempt => Assert.Equal(key, attempt.Key));
        }
    }

    private static readonly IdempotencySecret Secret =
        IdempotencySecret.ReadSecret(new ConfigurationBuilder().Build(), "Development", "tests");
}
