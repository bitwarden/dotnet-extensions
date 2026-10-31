using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Finishes a write its client has stopped waiting for: a request whose
/// method isn't safe (anything but <c>GET</c>, <c>HEAD</c>, <c>OPTIONS</c> and <c>TRACE</c>) sees
/// a <see cref="HttpContext.RequestAborted"/> that the client's leaving never cancels.
/// </summary>
/// <remarks>
/// <para>
/// A service's handlers take a cancellation token bound to <see cref="HttpContext.RequestAborted"/>
/// and pass it on to their queries and to the services they call. A client that sends a write and
/// closes its tab straight away (a proxy's <c>499</c>) would otherwise leave the write half done:
/// the first service's call to the next cancelled, or a service stopped after its first query. A
/// write is run to the end whether or not anybody is still there for the answer.
/// </para>
/// <para>
/// A safe request keeps the client's cancellation. It changes nothing, so stopping it loses
/// nothing the client asked for, and its work is spared. <see cref="IHttpRequestLifetimeFeature.Abort"/>
/// still aborts the connection: only the client's leaving is ignored. Stopping the host still
/// lets the request finish, within the host's shutdown timeout.
/// </para>
/// </remarks>
/// <param name="next">The rest of the pipeline.</param>
internal sealed class FinishedWrites(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http)
    {
        if (!HttpMethods.IsGet(http.Request.Method)
            && !HttpMethods.IsHead(http.Request.Method)
            && !HttpMethods.IsOptions(http.Request.Method)
            && !HttpMethods.IsTrace(http.Request.Method)
            && http.Features.Get<IHttpRequestLifetimeFeature>() is { } lifetime)
        {
            http.Features.Set<IHttpRequestLifetimeFeature>(new Unabandoned(lifetime));
        }

        return next(http);
    }

    /// <summary>
    /// The connection's lifetime, without the client's leaving.
    /// </summary>
    private sealed class Unabandoned(IHttpRequestLifetimeFeature connection) : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted
        {
            get => CancellationToken.None;
            set { }
        }

        public void Abort() => connection.Abort();
    }
}
