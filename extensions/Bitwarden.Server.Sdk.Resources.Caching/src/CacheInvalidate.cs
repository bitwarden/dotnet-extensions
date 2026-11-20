namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// A <c>cache.invalidate</c> message, read for nothing but its type: whatever it named is discarded.
/// </summary>
internal sealed record CacheInvalidate : CacheInvalidatePayload.ISole;
