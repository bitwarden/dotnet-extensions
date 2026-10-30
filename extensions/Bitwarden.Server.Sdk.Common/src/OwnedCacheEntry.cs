namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// How an entry in a service-owned cache is stored in Redis, shared by the service that writes it
/// and the clients that read it.
/// </summary>
/// <remarks>
/// <para>
/// One Redis hash per resource, under <c>{service}:{resource type}:{identifier or key}</c>. Only the
/// owning service writes under its own prefix. The fields:
/// </para>
/// <list type="table">
///   <item>
///     <term><c>v</c></term>
///     <description>The row's version when it was read, or when it changed.</description>
///   </item>
///   <item>
///     <term><c>o</c></term>
///     <description>The row's owner, as <c>{"organization":"…"}</c>; absent for a row nobody owns.</description>
///   </item>
///   <item>
///     <term><c>d</c></term>
///     <description>The resource object as the service serves it, cached attributes only.</description>
///   </item>
///   <item>
///     <term><c>none</c></term>
///     <description>Nothing is recorded under the key (a keyed read only).</description>
///   </item>
///   <item>
///     <term><c>gone</c></term>
///     <description>A tombstone: the row changed or was deleted at version <c>v</c>.</description>
///   </item>
/// </list>
/// <para>
/// The owner sits beside the data, so a reader checks it without trusting the payload. A tombstone
/// is a miss to a reader and refuses a fill of any older version, so a slow read can't put back a
/// value that a later change replaced.
/// </para>
/// <para>
/// This cache needs Redis itself, not any <c>IDistributedCache</c>: a fill reads the stored version and
/// writes in one atomic script, which a plain get and set can't do. It is separate from the caches
/// <c>Bitwarden.Server.Sdk.Caching</c> configures.
/// </para>
/// </remarks>
public static class OwnedCacheEntry
{
    /// <summary>
    /// The configuration key of the service cache's Redis connection.
    /// </summary>
    /// <remarks>
    /// When unset there is no service cache: owners fill nothing and clients always ask the service.
    /// </remarks>
    public const string ConnectionKey = "Caching:Owned:Redis:Configuration";

    /// <summary>
    /// The section a resource's switch lives in: <c>Caching:Owned:Resources:{service}:{type}:Enabled</c>.
    /// </summary>
    public const string ResourcesSection = "Caching:Owned:Resources";

    /// <summary>
    /// The version field.
    /// </summary>
    public const string Version = "v";

    /// <summary>
    /// The owner field.
    /// </summary>
    public const string Owner = "o";

    /// <summary>
    /// The data field.
    /// </summary>
    public const string Data = "d";

    /// <summary>
    /// The field marking that nothing is recorded under the key.
    /// </summary>
    public const string None = "none";

    /// <summary>
    /// The field marking a tombstone.
    /// </summary>
    public const string Gone = "gone";

    /// <summary>
    /// The OpenAPI extension that marks a read as cacheable for generated clients.
    /// </summary>
    public const string Extension = "x-bitwarden-cache";

    /// <summary>
    /// The placeholder for the identifier or key in a key template.
    /// </summary>
    public const string KeyPlaceholder = "{key}";

    /// <summary>
    /// The prefix every key a service owns starts with.
    /// </summary>
    /// <param name="service">The owning service's name.</param>
    public static string Prefix(string service) => $"{service}:";

    /// <summary>
    /// The key template a resource's entries are stored under.
    /// </summary>
    /// <param name="service">The owning service's name.</param>
    /// <param name="resourceType">The resource's type.</param>
    public static string Template(string service, string resourceType) => $"{service}:{resourceType}:{KeyPlaceholder}";

    /// <summary>
    /// The key of one entry.
    /// </summary>
    /// <param name="template">The resource's key template.</param>
    /// <param name="key">The identifier or key.</param>
    public static string Key(string template, Guid key) => template.Replace(KeyPlaceholder, key.ToString("D"), StringComparison.Ordinal);
}
