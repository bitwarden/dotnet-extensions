namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// The outbox message <c>cache.invalidate</c>, which this package never records: a change invalidates its entries
/// just before it commits.
/// </summary>
/// <remarks>
/// A service whose outbox holds one from a build that recorded it discards it. That change committed and invalidated
/// its entries straight after, or, where that failed, its entries have long since expired.
/// </remarks>
internal static class RetiredCacheInvalidation
{
    public const string Type = "cache.invalidate";
}
