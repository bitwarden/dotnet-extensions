using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

// A write runs to the end when the client leaves, as a handler that takes no cancellation does; a
// safe request may still stop.
public sealed class FinishedWritesTests
{
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task AWrite_WhoseClientHasLeft_IsNotCancelled(string method)
    {
        var connection = new Connection();
        connection.Leave();

        var seen = await Seen(method, connection);

        Assert.False(seen.CanBeCanceled);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public async Task ASafeRequest_WhoseClientHasLeft_IsCancelled(string method)
    {
        var connection = new Connection();
        connection.Leave();

        var seen = await Seen(method, connection);

        Assert.True(seen.IsCancellationRequested);
    }

    // Only the client's leaving is ignored: a handler that aborts the connection still does.
    [Fact]
    public async Task AWrite_AbortingTheConnection_StillAbortsIt()
    {
        var connection = new Connection();
        var http = Context("POST", connection);

        await new FinishedWrites(context =>
        {
            context.Abort();
            return Task.CompletedTask;
        }).InvokeAsync(http);

        Assert.True(connection.Aborted);
    }

    private static async Task<CancellationToken> Seen(string method, Connection connection)
    {
        var http = Context(method, connection);
        var seen = default(CancellationToken);

        await new FinishedWrites(context =>
        {
            seen = context.RequestAborted;
            return Task.CompletedTask;
        }).InvokeAsync(http);

        return seen;
    }

    private static DefaultHttpContext Context(string method, Connection connection)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Features.Set<IHttpRequestLifetimeFeature>(connection);

        return http;
    }

    /// <summary>
    /// A connection whose client can leave, recording whether it was aborted.
    /// </summary>
    private sealed class Connection : IHttpRequestLifetimeFeature
    {
        private readonly CancellationTokenSource _left = new();

        public bool Aborted { get; private set; }

        public CancellationToken RequestAborted
        {
            get => _left.Token;
            set { }
        }

        public void Leave() => _left.Cancel();

        public void Abort() => Aborted = true;
    }
}
