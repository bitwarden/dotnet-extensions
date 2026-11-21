using Bitwarden.Server.Sdk.Authorization;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// One operation as the cache contract sees it.
/// </summary>
/// <param name="Name">How the operation is named in a refusal: its class and method.</param>
/// <param name="Method">The handler.</param>
/// <param name="Settled">What the platform settled it demands, or null where nothing is settled.</param>
/// <param name="Filters">How many endpoint filters stand between the request and the handler, not counting the platform's.</param>
/// <param name="Metadata">The endpoint's metadata, for what else it demands of a caller.</param>
internal sealed record CacheOperation(
    string Name,
    System.Reflection.MethodInfo Method,
    AuthorizationAttribute? Settled,
    int Filters = 0,
    IReadOnlyList<object>? Metadata = null)
{
    public T? Attribute<T>() where T : Attribute => Method.GetCustomAttributes(typeof(T), inherit: true).OfType<T>().FirstOrDefault();
}
