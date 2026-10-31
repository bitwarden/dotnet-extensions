namespace Bitwarden.Server.Sdk.Service.Http;

/// <summary>
/// Where per-address request counts are kept: in this process, or in a Redis every instance of the
/// service shares (<see cref="DistributedIpRateLimitingOptions"/>).
/// </summary>
/// <remarks>
/// A count is a fixed window that opens with its first request and lasts the rule's period. The
/// per-address limits (<see cref="IpRateLimitOptions"/>) count here, and so may a service's own
/// limits, such as a registration allowance.
/// </remarks>
public interface IRateLimitCounters
{
    /// <summary>
    /// Counts one request in a window.
    /// </summary>
    /// <param name="bucket">
    /// What is counted: the rule and the client address, for example. Redis holds it only as an
    /// unsalted SHA-256 hash in a key that expires with its window.
    /// </param>
    /// <param name="limit">How many requests the window allows.</param>
    /// <param name="period">How long the window lasts from its first request.</param>
    /// <param name="cancellationToken">Cancels the count.</param>
    /// <returns>True while the window's count is within <paramref name="limit"/>; false over it.</returns>
    ValueTask<bool> TryTakeAsync(string bucket, int limit, TimeSpan period, CancellationToken cancellationToken = default);
}
