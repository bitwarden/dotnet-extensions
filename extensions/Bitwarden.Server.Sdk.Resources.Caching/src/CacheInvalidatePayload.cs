using Bitwarden.Server.Sdk.MessageBroker;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// <see cref="CacheInvalidate"/> as a payload family, handled only in the service that recorded it.
/// </summary>
internal sealed class CacheInvalidatePayload
    : Payload<CacheInvalidatePayload, CacheInvalidate, CacheInvalidate>,
      IPayloadVariants<CacheInvalidatePayload>,
      IPayloadWireName<CacheInvalidatePayload>
{
    public static string WireName => RetiredCacheInvalidation.Type;

    public static IReadOnlyList<(Type Type, string WireName)> Variants => [(typeof(CacheInvalidate), "v1")];
}
