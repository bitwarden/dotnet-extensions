using Bitwarden.Server.Sdk.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Begins a <see cref="RequestMemo"/> for each inbound HTTP request, and ends it when the request ends: a service's
/// clients answer the request's repeated reads of other services from it.
/// </summary>
/// <remarks>
/// <para>
/// None for an operation marked <see cref="WithoutRequestMemoAttribute"/>, none for a request that upgrades its
/// connection, a WebSocket among them (a connection lives far longer than any answer stays true), and none while <see cref="RequestMemo.EnabledKey"/> is <c>false</c>. The bounds
/// are <see cref="RequestMemo.MaxEntriesKey"/> and <see cref="RequestMemo.MaxBytesKey"/>, read once when the service
/// starts.
/// </para>
/// <para>
/// The memo flows on the execution context, so every scope the request opens shares it, and nothing outside the request
/// sees it. Work the request starts and doesn't await finds it ended once the request is answered, and calls as if there
/// were none.
/// </para>
/// </remarks>
/// <param name="next">The rest of the pipeline.</param>
/// <param name="configuration">The switch and the bounds.</param>
internal sealed class RequestMemoScope(RequestDelegate next, IConfiguration configuration)
{
    private readonly int _maxEntries = int.TryParse(configuration[RequestMemo.MaxEntriesKey], out var entries) && entries >= 0
        ? entries
        : RequestMemo.DefaultMaxEntries;

    private readonly long _maxBytes = long.TryParse(configuration[RequestMemo.MaxBytesKey], out var bytes) && bytes >= 0
        ? bytes
        : RequestMemo.DefaultMaxBytes;

    public async Task InvokeAsync(HttpContext http)
    {
        if (string.Equals(configuration[RequestMemo.EnabledKey], "false", StringComparison.OrdinalIgnoreCase)
            || Upgrades(http)
            || http.GetEndpoint()?.Metadata.GetMetadata<WithoutRequestMemoAttribute>() is not null)
        {
            await next(http);
            return;
        }

        using var memo = RequestMemo.Begin(_maxEntries, _maxBytes);
        await next(http);
    }

    // Read from the server's own features: IsWebSocketRequest is set only once UseWebSockets has run, and the platform's
    // pipeline doesn't run it (a SignalR hub runs it inside its own endpoint, after this). HTTP/1.1 upgrades the
    // connection; HTTP/2 opens a WebSocket with an extended CONNECT.
    private static bool Upgrades(HttpContext http) =>
        http.Features.Get<IHttpUpgradeFeature>()?.IsUpgradableRequest == true
        || http.Features.Get<IHttpExtendedConnectFeature>()?.IsExtendedConnect == true
        || http.WebSockets.IsWebSocketRequest;
}
