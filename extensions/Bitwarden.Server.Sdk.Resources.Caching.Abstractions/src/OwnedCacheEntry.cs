namespace Bitwarden.Server.Sdk.Resources.Caching;

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
    /// <remarks>
    /// <c>false</c> stops the owner filling that resource, index or answer and its clients reading it,
    /// so they ask the owner. Unset: it is cached.
    /// </remarks>
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

    // The segment answers are kept under (OwnedAnswerKey.Template), so no resource type may take it.
    internal const string AnswerResourceType = "answer";

    // Segments a service's other keys sit under in the same prefix (an index and its generation, idempotency records):
    // a resource type taking one would mix its entries with them in a scan of that segment.
    private static readonly string[] ReservedResourceTypes = [AnswerResourceType, "index", "gen", "idempotency"];

    /// <summary>
    /// The prefix every key a service owns starts with.
    /// </summary>
    /// <param name="service">The owning service's name. It may not contain <c>:</c>, <c>{</c> or <c>}</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="service"/> is empty, or contains <c>:</c>, <c>{</c> or <c>}</c>.</exception>
    public static string Prefix(string service)
    {
        ThrowIfNotAName(service);
        return $"{service}:";
    }

    /// <summary>
    /// The key template a resource's entries are stored under.
    /// </summary>
    /// <param name="service">The owning service's name. It may not contain <c>:</c>, <c>{</c> or <c>}</c>.</param>
    /// <param name="resourceType">The resource's type. It may not contain <c>:</c>, <c>{</c> or <c>}</c>, nor be <c>answer</c>, <c>index</c>, <c>gen</c> or <c>idempotency</c>.</param>
    /// <exception cref="ArgumentException">A name is empty or contains <c>:</c>, <c>{</c> or <c>}</c>, or <paramref name="resourceType"/> is reserved.</exception>
    public static string Template(string service, string resourceType)
    {
        ThrowIfNotAName(resourceType);
        if (ReservedResourceTypes.Contains(resourceType, StringComparer.Ordinal))
        {
            throw new ArgumentException($"The resource type '{resourceType}' is reserved for the service's other keys.", nameof(resourceType));
        }

        return $"{Prefix(service)}{resourceType}:{KeyPlaceholder}";
    }

    /// <summary>
    /// The key of one entry.
    /// </summary>
    /// <param name="template">The resource's key template.</param>
    /// <param name="key">The identifier or key.</param>
    public static string Key(string template, Guid key) => template.Replace(KeyPlaceholder, key.ToString("D"), StringComparison.Ordinal);

    // A name between the separators of a key or template: a ':' would let two different names
    // give the same key, and a brace would put a placeholder where the name should be.
    internal static void ThrowIfNotAName(string name, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(name))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, paramName);

        if (name.AsSpan().IndexOfAny(':', '{', '}') >= 0)
        {
            throw new ArgumentException("A name in a key may not contain ':', '{' or '}'.", paramName);
        }
    }
}
