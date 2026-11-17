using System.Diagnostics.Metrics;

namespace Bitwarden.Server.Sdk.Authorization.CallChains;

/// <summary>
/// Counts re-entries: calls into a service whose own request was still waiting on the call that led here.
/// </summary>
internal sealed class CallChainMetrics
{
    /// <summary>
    /// The meter's name, collected with the platform's other <c>Bitwarden.*</c> meters.
    /// </summary>
    /// <remarks>
    /// Not this package's name: dashboards and alerts select the meter by this name, so it stays as it is.
    /// </remarks>
    internal const string MeterName = "Bitwarden.Server.Sdk.Service.CallChains";

    /// <summary>
    /// The counter's name.
    /// </summary>
    internal const string ReentriesName = "bitwarden.call.reentries";

    /// <summary>
    /// The tag naming the chain, this service last: service names only, so its values are few.
    /// </summary>
    internal const string ChainTag = "bitwarden.call.chain";

    /// <summary>
    /// The tag saying whether the operation declares the re-entry accepted.
    /// </summary>
    internal const string AcceptedTag = "bitwarden.call.reentry.accepted";

    /// <summary>
    /// The tag naming the operation by its route template, never its path.
    /// </summary>
    internal const string RouteTag = "http.route";

    private readonly Counter<long> _reentries;

    public CallChainMetrics(IMeterFactory meters)
    {
        _reentries = meters.Create(MeterName).CreateCounter<long>(
            ReentriesName,
            unit: "{reentry}",
            description: "Calls into a service whose own request was still waiting on the call that led to them.");
    }

    public void Reentered(string chain, string route, bool accepted) =>
        _reentries.Add(
            1,
            new KeyValuePair<string, object?>(ChainTag, chain),
            new KeyValuePair<string, object?>(RouteTag, route),
            new KeyValuePair<string, object?>(AcceptedTag, accepted));
}
