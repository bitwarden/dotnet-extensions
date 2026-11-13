using System.Net;
using Bitwarden.Server.Sdk.Errors;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// What a caller is told when the service it called failed, by how it failed.
/// </summary>
public sealed class ServiceFaultHandlerTests
{
    private const string Service = "widget-service";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Send_ServiceCannotBeReached_UnavailableNamingIt()
    {
        var invoker = Invoker(_ => throw new HttpRequestException("Connection refused (widget.internal:443)"));

        var actual = await Assert.ThrowsAsync<ServiceUnavailableException>(() => Send(invoker));

        Assert.Equal(Service, actual.Service);
        Assert.Equal("The widget-service service could not be reached.", actual.Detail);
    }

    // What the caller is told is written here, never taken from the failure: no address leaks.
    [Fact]
    public async Task Send_ServiceCannotBeReached_DetailCarriesNothingOfTheFailure()
    {
        var invoker = Invoker(_ => throw new HttpRequestException("Connection refused (widget.internal:443)"));

        var actual = await Assert.ThrowsAsync<ServiceUnavailableException>(() => Send(invoker));

        Assert.DoesNotContain("widget.internal", actual.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_ServiceDoesNotAnswerInTime_UnavailableNamingIt()
    {
        var invoker = Invoker(async cancellation =>
        {
            await Task.Delay(Timeout.Infinite, cancellation);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, TimeSpan.FromMilliseconds(50));

        var actual = await Assert.ThrowsAsync<ServiceUnavailableException>(() => Send(invoker));

        Assert.Equal("The widget-service service did not answer within 0.05 seconds.", actual.Detail);
    }

    // A caller that stops waiting is not the dependency's fault, and is not reported as it.
    [Fact]
    public async Task Send_CallerCancels_CancelledRatherThanUnavailable()
    {
        using var caller = new CancellationTokenSource();
        var invoker = Invoker(async cancellation =>
        {
            await caller.CancelAsync();
            await Task.Delay(Timeout.Infinite, cancellation);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Send(invoker, caller.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Send_ServiceAnswersThatItIsUnavailable_UnavailableNamingIt(HttpStatusCode status)
    {
        var invoker = Invoker(_ => Task.FromResult(new HttpResponseMessage(status)));

        var actual = await Assert.ThrowsAsync<ServiceUnavailableException>(() => Send(invoker));

        Assert.Equal($"The widget-service service answered {(int)status}.", actual.Detail);
    }

    // Up, and with a bug of its own. Reporting that as an outage would hide the defect.
    [Fact]
    public async Task Send_ServiceAnswers500_AFailureNotUnavailable()
    {
        var invoker = Invoker(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var actual = await Assert.ThrowsAsync<ServiceCallException>(() => Send(invoker));

        Assert.Equal(HttpStatusCode.InternalServerError, actual.Status);
    }

    [Fact]
    public async Task Send_ServiceAnswersSuccessfully_TheResponse()
    {
        var invoker = Invoker(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        using var actual = await Send(invoker);

        Assert.Equal(HttpStatusCode.OK, actual.StatusCode);
    }

    // Every reason a request was refused, not the first: a caller passing the refusal on must be
    // able to report all of them, as the standards ask.
    [Fact]
    public async Task Send_ServiceRefusesForSeveralReasons_EveryOneIsKept()
    {
        var invoker = Invoker(_ => Task.FromResult(Refused(
            """{"errors":[{"status":"422","code":"invalid-value","detail":"First.","source":{"pointer":"/data/0/attributes/folder"}},{"status":"422","code":"invalid-value","detail":"Second.","source":{"pointer":"/data/2/attributes/folder"}}]}""")));

        var actual = await Assert.ThrowsAsync<RequestRefusedException>(() => Send(invoker));

        Assert.Equal(
            ["/data/0/attributes/folder", "/data/2/attributes/folder"],
            actual.Errors.Select(error => error.Source?.Pointer));
        Assert.Equal(422, ((IProvideErrors)actual).StatusCode);
    }

    [Fact]
    public async Task Send_ServiceRefusesForOneReason_InvalidRequestCarryingIt()
    {
        var invoker = Invoker(_ => Task.FromResult(Refused(
            """{"errors":[{"status":"422","code":"invalid-value","detail":"Only.","source":{"pointer":"/ids"}}]}""")));

        var actual = await Assert.ThrowsAsync<InvalidRequestException>(() => Send(invoker));

        Assert.Equal("/ids", actual.Pointer);
    }

    // A service's own codes are left for its client to interpret, as before.
    [Fact]
    public async Task Send_ServiceRefusesWithCodesOfItsOwn_LeftForItsClient()
    {
        var invoker = Invoker(_ => Task.FromResult(Refused(
            """{"errors":[{"status":"422","code":"salt-mismatch"},{"status":"422","code":"kdf-mismatch"}]}""")));

        var actual = await Assert.ThrowsAsync<ServiceCallException>(() => Send(invoker));

        Assert.Equal(2, actual.Response?.Errors.Count);
    }

    // A person's keys rotating: relayed with the status and errors it came with,
    // a 503 included, rather than reported as the dependency being unavailable.
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Send_KeyRotationInProgress_RelayedAsAnswered(HttpStatusCode status)
    {
        var invoker = Invoker(_ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(
                $$"""{"errors":[{"status":"{{(int)status}}","code":"key-rotation-in-progress","detail":"Rotating."}]}""",
                System.Text.Encoding.UTF8,
                "application/json"),
        }));

        var actual = await Assert.ThrowsAsync<RelayedRefusalException>(() => Send(invoker));

        Assert.Equal((int)status, actual.StatusCode);
        Assert.Equal("key-rotation-in-progress", Assert.Single(actual.Errors).Code);
    }

    // Any other conflict is still the platform's conflict, its document read only once.
    [Fact]
    public async Task Send_AnotherConflict_StillAConflict()
    {
        var invoker = Invoker(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                """{"errors":[{"status":"409","code":"conflict","detail":"Changed meanwhile."}]}""",
                System.Text.Encoding.UTF8,
                "application/json"),
        }));

        var actual = await Assert.ThrowsAsync<ConflictException>(() => Send(invoker));

        Assert.Equal("Changed meanwhile.", actual.Message);
    }

    // A callee that answers a refusal's status and then stalls its error document hasn't answered in time: the
    // deadline covers reading the document too.
    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Send_RefusalWhoseDocumentStalls_UnavailableWithinTheDeadline(HttpStatusCode status)
    {
        var invoker = Invoker(_ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StreamContent(new Stalling()),
        }), TimeSpan.FromMilliseconds(50));

        var actual = await Assert.ThrowsAsync<ServiceUnavailableException>(() => Send(invoker))
            .WaitAsync(TimeSpan.FromSeconds(10), Token);

        Assert.Equal("The widget-service service did not answer within 0.05 seconds.", actual.Detail);
    }

    /// <summary>
    /// A body that never arrives: every read waits until it is cancelled.
    /// </summary>
    private sealed class Stalling : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static HttpResponseMessage Refused(string body) =>
        new(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

    private static Task<HttpResponseMessage> Send(HttpMessageInvoker invoker, CancellationToken? cancellation = null) =>
        invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://widget.internal/api/v1/memberships"), cancellation ?? Token);

    private static HttpMessageInvoker Invoker(
        Func<CancellationToken, Task<HttpResponseMessage>> answer,
        TimeSpan? timeout = null) =>
        new(new ServiceFaultHandler(Service, timeout ?? TimeSpan.FromSeconds(30)) { InnerHandler = new Answering(answer) });

    private sealed class Answering(Func<CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            answer(cancellationToken);
    }
}
