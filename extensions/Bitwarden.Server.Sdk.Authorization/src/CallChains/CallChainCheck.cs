using System.Diagnostics;
using Bitwarden.Server.Sdk.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Authorization.CallChains;

/// <summary>
/// Reads the services a call passed through (<see cref="BitwardenHeaders.CallChain"/>), and notices when this service
/// is among them: a re-entry, a synchronous loop-back into a service whose own request is still waiting on it.
/// </summary>
/// <remarks>
/// <para>
/// The chain is believed only from another service's authenticated call (<see cref="InternalCaller"/>). From anybody
/// else, a person's browser included, the header is removed and the request has no chain, as a product service has
/// no business with any header that only services send. So it runs after authorization, when an operation that names
/// its own authentication scheme has authenticated its caller too.
/// </para>
/// <para>
/// The chain becomes the request's (<see cref="CallChain.Current"/>), so the calls it makes carry it on with this
/// service added, and the request's span carries it (<c>bitwarden.call.chain</c>). A re-entry is counted
/// (<c>bitwarden.call.reentries</c>, tagged with the chain and the route), marked on the span and logged as a warning
/// with the chain; with <see cref="CallChainOptions.RefuseReentry"/> it is refused, unless the operation declares it
/// accepted (<see cref="AcceptsReentryAttribute"/>), which is counted and logged at information.
/// </para>
/// <para>
/// Never a re-entry: Identity's token endpoint, and the health, readiness and startup probes. A token or context
/// exchange, and a probe, is the platform's plumbing, not a call back into a service's business.
/// </para>
/// </remarks>
/// <param name="next">The rest of the pipeline.</param>
internal sealed class CallChainCheck(RequestDelegate next)
{
    /// <summary>
    /// The span tag naming the chain the call arrived with.
    /// </summary>
    internal const string ChainSpanTag = "bitwarden.call.chain";

    /// <summary>
    /// The span tag marking a re-entry.
    /// </summary>
    internal const string ReentrySpanTag = "bitwarden.call.reentry";

    /// <summary>
    /// The paths never treated as a re-entry.
    /// </summary>
    internal static readonly IReadOnlyList<PathString> Excluded =
        ["/connect/token", "/api/v1/health", "/api/v1/readiness", "/api/v1/startup"];

    public async Task InvokeAsync(HttpContext http)
    {
        if (Read(http) is not { } chain)
        {
            await next(http);
            return;
        }

        Activity.Current?.SetTag(ChainSpanTag, CallChain.Format(chain));

        var self = http.RequestServices.GetRequiredService<IServiceIdentity>().Name;

        if (chain.Contains(self, StringComparer.Ordinal) && !IsExcluded(http.Request.Path))
        {
            Reentered(http, chain, self);
        }

        using (CallChain.Enter(chain))
        {
            await next(http);
        }
    }

    /// <summary>
    /// The chain a request arrived with, from another service only; the header is removed whoever sent it.
    /// </summary>
    internal static IReadOnlyList<string>? Read(HttpContext http)
    {
        var headers = http.Request.Headers;

        if (!headers.TryGetValue(BitwardenHeaders.CallChain, out var values))
        {
            return null;
        }

        headers.Remove(BitwardenHeaders.CallChain);

        return InternalCaller.Is(http.User) && values.Count == 1 && CallChain.TryParse(values[0], out var chain)
            ? chain
            : null;
    }

    /// <summary>
    /// Whether a path is the platform's plumbing, never a re-entry.
    /// </summary>
    internal static bool IsExcluded(PathString path) =>
        Excluded.Any(excluded => path.StartsWithSegments(excluded, StringComparison.OrdinalIgnoreCase));

    private static void Reentered(HttpContext http, IReadOnlyList<string> chain, string self)
    {
        var services = http.RequestServices;
        var endpoint = http.GetEndpoint();
        var accepted = endpoint?.Metadata.GetMetadata<AcceptsReentryAttribute>();
        var loop = CallChain.Format([.. chain, self]);
        var route = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? "(unmatched)";
        var operation = endpoint?.DisplayName ?? route;

        services.GetRequiredService<CallChainMetrics>().Reentered(loop, route, accepted is not null);
        Activity.Current?.SetTag(ReentrySpanTag, true);

        var logger = services.GetRequiredService<ILogger<CallChainCheck>>();

        if (accepted is not null)
        {
            logger.LogInformation(
                "Accepted re-entry: {Operation} was called back while a request of this service waited on the call. Call chain: {CallChain}. Accepted because: {Reason}",
                operation,
                loop,
                accepted.Reason);
            return;
        }

        logger.LogWarning(
            "Re-entry: {Operation} was called back while a request of this service waited on the call. Call chain: {CallChain}. Remove the loop-back, or declare it with [AcceptsReentry].",
            operation,
            loop);

        if (services.GetRequiredService<IOptionsMonitor<CallChainOptions>>().CurrentValue.RefuseReentry)
        {
            throw new CallReentryException(
                $"Refused a re-entry into {operation}: it was called back while a request of this service waited on the call (call chain {loop}). "
                + "Remove the loop-back, or declare it with [AcceptsReentry]; CallChain:RefuseReentry is on.");
        }
    }
}
