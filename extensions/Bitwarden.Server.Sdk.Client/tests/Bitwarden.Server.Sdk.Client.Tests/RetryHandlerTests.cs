using System.Diagnostics;
using System.Net;
using Bitwarden.Server.Sdk.Errors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static Bitwarden.Server.Sdk.Client.Tests.PipelineHarness;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// A call tried again when the callee wasn't there, within the call's deadline, and only when it is safe to send twice.
/// </summary>
public sealed class RetryHandlerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // Every attempt of one write carries the one key and the same body, so the callee does it at most once.
    [Fact]
    public async Task AWrite_RetriedAfterAFailedConnection_KeepsItsKeyAndBody()
    {
        var callee = new Scripted(Refused, BareStatus(HttpStatusCode.BadGateway), Ok);
        await using var services = Services(callee);

        using var response = await services.GetRequiredService<Probe>().SendAsync(HttpMethod.Post, Token, body: new { note = "n" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, callee.Attempts.Count);
        Assert.Single(callee.Attempts.Select(attempt => attempt.Key).Distinct());
        Assert.All(callee.Attempts, attempt => Assert.Equal("""{"note":"n"}""", attempt.Body));
    }

    // A write without a key (its operation doesn't accept one) goes again only when its connection never opened, so
    // it can't have arrived.
    [Fact]
    public async Task AnUnkeyedWrite_WhoseConnectionNeverOpened_IsRetried()
    {
        var callee = new Scripted(NeverOpened, Ok);
        await using var services = Services(callee);

        using var response = await services.GetRequiredService<Probe>().SendAsync(HttpMethod.Put, Token, address: "settings/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callee.Attempts.Count);
        Assert.All(callee.Attempts, attempt => Assert.Null(attempt.Key));
    }

    // Cut off, or answered by a proxy, the write may have arrived: sent once.
    [Theory]
    [InlineData(HttpRequestError.ResponseEnded)]
    [InlineData(HttpRequestError.HttpProtocolError)]
    [InlineData(HttpRequestError.ConnectionError)]
    public async Task AnUnkeyedWrite_ThatMayHaveArrived_IsNotRetried(HttpRequestError error)
    {
        var callee = new Scripted(_ => throw new HttpRequestException(error, "cut off"), Ok);
        await using var services = Services(callee);

        await Assert.ThrowsAnyAsync<Exception>(
            () => services.GetRequiredService<Probe>().SendAsync(HttpMethod.Put, Token, address: "settings/1"));

        Assert.Single(callee.Attempts);
    }

    [Fact]
    public async Task AnUnkeyedWrite_AnsweredUnavailableFromInFrontOfTheCallee_IsNotRetried()
    {
        var callee = new Scripted(BareStatus(HttpStatusCode.BadGateway), Ok);
        await using var services = Services(callee);

        await Assert.ThrowsAnyAsync<Exception>(
            () => services.GetRequiredService<Probe>().SendAsync(HttpMethod.Put, Token, address: "settings/1"));

        Assert.Single(callee.Attempts);
    }

    [Theory]
    [InlineData(System.Net.Sockets.SocketError.ConnectionRefused, true)]
    [InlineData(System.Net.Sockets.SocketError.HostUnreachable, true)]
    [InlineData(System.Net.Sockets.SocketError.NetworkUnreachable, true)]
    [InlineData(System.Net.Sockets.SocketError.HostNotFound, true)]
    [InlineData(System.Net.Sockets.SocketError.ConnectionReset, false)]
    [InlineData(System.Net.Sockets.SocketError.TimedOut, false)]
    public void NeverSent_AConnectionError_OnlyWhenItNeverOpened(System.Net.Sockets.SocketError socket, bool expected) =>
        Assert.Equal(
            expected,
            Transient.NeverSent(new HttpRequestException(
                HttpRequestError.ConnectionError, "failed", new System.Net.Sockets.SocketException((int)socket))));

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, true)]
    [InlineData(HttpRequestError.ProxyTunnelError, true)]
    [InlineData(HttpRequestError.ConnectionError, false)]
    [InlineData(HttpRequestError.ResponseEnded, false)]
    [InlineData(HttpRequestError.SecureConnectionError, false)]
    public void NeverSent_ByTheError(HttpRequestError error, bool expected) =>
        Assert.Equal(expected, Transient.NeverSent(new HttpRequestException(error, "failed")));

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task ARead_AnsweredUnavailableFromInFrontOfTheCallee_IsRetried(HttpStatusCode status)
    {
        var callee = new Scripted(BareStatus(status), Ok);
        await using var services = Services(callee);

        using var response = await services.GetRequiredService<Probe>().SendAsync(HttpMethod.Get, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callee.Attempts.Count);
    }

    [Fact]
    public async Task ACalleeNeverThere_IsTriedThreeTimesThenUnavailable()
    {
        var callee = new Scripted(BareStatus(HttpStatusCode.BadGateway));
        await using var services = Services(callee);

        var unavailable = await Assert.ThrowsAsync<ServiceUnavailableException>(
            () => services.GetRequiredService<Probe>().SendAsync(HttpMethod.Get, Token));

        Assert.Equal(Callee, unavailable.Service);
        Assert.Equal(3, callee.Attempts.Count);
    }

    // The callee's own answers are its answers: its 503 (a dependency down, a key rotation) and its 500 are not retried.
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task TheCalleesOwnAnswer_IsNotRetried(HttpStatusCode status)
    {
        var callee = new Scripted(ErrorDocument(status));
        await using var services = Services(callee);

        await Assert.ThrowsAnyAsync<Exception>(() => services.GetRequiredService<Probe>().SendAsync(HttpMethod.Get, Token));

        Assert.Single(callee.Attempts);
    }

    // The callee still running an earlier attempt of this write (409 with Retry-After): waited for, then the answer.
    [Fact]
    public async Task AWrite_StillRunningAtTheCallee_IsWaitedFor()
    {
        var callee = new Scripted(StillRunning, Ok);
        await using var services = Services(callee);

        using var response = await services.GetRequiredService<Probe>().SendAsync(HttpMethod.Post, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callee.Attempts.Count);
    }

    // Still running after every retry: the callee hasn't answered yet, which is it being unavailable, not a conflict.
    [Fact]
    public async Task AWrite_StillRunningAfterTheRetries_IsUnavailable()
    {
        var callee = new Scripted(StillRunning);
        await using var services = Services(callee);

        var unavailable = await Assert.ThrowsAsync<ServiceUnavailableException>(
            () => services.GetRequiredService<Probe>().SendAsync(HttpMethod.Post, Token));

        Assert.Contains("still finishing an earlier attempt", unavailable.Detail, StringComparison.Ordinal);
        Assert.Equal(3, callee.Attempts.Count);
    }

    // Still running after every retry, or with no retries at all, the first attempt may yet land: not a refusal, so a
    // step guarded with UndoOn.Refusal keeps the writes made before it.
    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    public async Task AWrite_StillRunningWhenTheCallEnds_IsNotARefusal_AndKeepsTheWritesBefore(int retries)
    {
        var callee = new Scripted(StillRunning);
        await using var services = Services(callee, configure: options => options.Retries = retries);
        var undone = 0;
        var undo = new Undo(NullLogger.Instance);
        undo.Add("an earlier write", _ => Task.FromResult(++undone));

        var failure = await Assert.ThrowsAsync<ServiceUnavailableException>(() => undo.GuardAsync(
            () => services.GetRequiredService<Probe>().SendAsync(HttpMethod.Post, Token),
            UndoOn.Refusal));

        Assert.False(Undo.IsRefusal(failure));
        Assert.Equal(0, undone);
        Assert.True(undo.Any);
        Assert.Equal(retries + 1, callee.Attempts.Count);
    }

    // A real conflict (a stale version, a duplicate) is the callee refusing: the writes before it are taken back.
    [Fact]
    public async Task AWrite_RefusedAsAConflict_IsARefusal_AndTakesBackTheWritesBefore()
    {
        var callee = new Scripted(Conflict);
        await using var services = Services(callee);
        var undone = 0;
        var undo = new Undo(NullLogger.Instance);
        undo.Add("an earlier write", _ => Task.FromResult(++undone));

        var failure = await Assert.ThrowsAsync<ConflictException>(() => undo.GuardAsync(
            () => services.GetRequiredService<Probe>().SendAsync(HttpMethod.Post, Token),
            UndoOn.Refusal));

        Assert.True(Undo.IsRefusal(failure));
        Assert.Equal(1, undone);
        Assert.False(undo.Any);
        Assert.Single(callee.Attempts);
    }

    // A conflict without Retry-After is the callee's answer.
    [Fact]
    public async Task AConflict_IsTheCalleesAnswer()
    {
        var callee = new Scripted(ErrorDocument(HttpStatusCode.Conflict));
        await using var services = Services(callee);

        await Assert.ThrowsAsync<ServiceCallException>(() => services.GetRequiredService<Probe>().SendAsync(HttpMethod.Post, Token));

        Assert.Single(callee.Attempts);
    }

    // A TLS failure is configuration: the same every time.
    [Fact]
    public async Task ATlsFailure_IsNotRetried()
    {
        var callee = new Scripted(_ => throw new HttpRequestException(HttpRequestError.SecureConnectionError, "untrusted"));
        await using var services = Services(callee);

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => services.GetRequiredService<Probe>().SendAsync(HttpMethod.Get, Token));

        Assert.Single(callee.Attempts);
    }

    // A write without a key is never sent twice: the callee might have done it.
    [Fact]
    public async Task AWriteWithoutAKey_IsNotRetried()
    {
        var callee = new Scripted(Refused, Ok);
        using var client = new HttpClient(new RetryHandler(2, TimeSpan.Zero, TimeProvider.System) { InnerHandler = callee });

        await Assert.ThrowsAsync<HttpRequestException>(() => client.PostAsync("http://callee.invalid/things", null, Token));

        Assert.Single(callee.Attempts);
    }

    // Retries and their waits are inside the call's one deadline.
    [Fact]
    public async Task Retries_StayWithinTheDeadline()
    {
        var callee = new Scripted(Refused);
        await using var services = Services(callee, configure: options =>
        {
            options.Timeout = TimeSpan.FromMilliseconds(300);
            options.RetryDelay = TimeSpan.FromSeconds(30);
        });
        var watch = Stopwatch.StartNew();

        var unavailable = await Assert.ThrowsAsync<ServiceUnavailableException>(
            () => services.GetRequiredService<Probe>().SendAsync(HttpMethod.Get, Token));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed}.");
        Assert.Contains("did not answer within 0.3 seconds", unavailable.Detail, StringComparison.Ordinal);
    }

    // A statement of whom a call is about covers the call, all its attempts.
    [Fact]
    public async Task AStatedCall_Retried_KeepsItsStatement()
    {
        var callee = new Scripted(Refused, Ok);
        await using var services = Services(callee);
        var organization = Guid.NewGuid();

        using (OutgoingCall.AsSystem(organization))
        {
            using var response = await services.GetRequiredService<Probe>().SendAsync(HttpMethod.Post, Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.All(callee.Attempts, attempt => Assert.Equal(organization.ToString(), attempt.Organization));
    }

    [Fact]
    public async Task NoRetries_SendsOnce()
    {
        var callee = new Scripted(Refused, Ok);
        await using var services = Services(callee, configure: options => options.Retries = 0);

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => services.GetRequiredService<Probe>().SendAsync(HttpMethod.Get, Token));

        Assert.Single(callee.Attempts);
    }

    [Theory]
    [InlineData(3, 0, 5)]
    [InlineData(-1, 0, 5)]
    [InlineData(2, -1, 5)]
    [InlineData(2, 0, -1)]
    public void Options_OutOfBounds_AreRefused(int retries, int delayMilliseconds, int breakAfter)
    {
        var result = new BitwardenClientOptionsValidator(Callee).Validate(Callee, new BitwardenClientOptions
        {
            BaseAddress = "http://callee.invalid",
            CallerName = "caller",
            IdentityUri = "http://identity.invalid",
            CallerSecret = "made-up",
            Retries = retries,
            RetryDelay = TimeSpan.FromMilliseconds(delayMilliseconds),
            BreakAfter = breakAfter,
        });

        Assert.True(result.Failed);
    }
}
