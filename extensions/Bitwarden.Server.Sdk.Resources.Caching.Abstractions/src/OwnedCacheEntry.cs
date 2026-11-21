namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// How an entry in a service-owned cache is stored in Redis, shared by the service that writes it
/// and the clients that read it.
/// </summary>
/// <remarks>
/// <para>
/// One Redis hash per resource, under <c>{service}:{resource type}:{identifier or key}</c>. A host writes
/// only under its own service's prefix. The fields:
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
///     <description>
///     The resource object as the service serves it to a caller holding the read scope: every attribute but those
///     worked out per caller. For a cached read-many (<see cref="ReadManyTemplate"/>), the array of them, in its order.
///     </description>
///   </item>
///   <item>
///     <term><c>f</c></term>
///     <description>
///     The format the entry is written in (<see cref="CurrentFormat"/>), on a resource's entry; a reader that doesn't
///     know it treats the entry as a miss.
///     </description>
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
/// The owner sits beside the data, so a reader checks it without trusting the payload. A reader
/// treats a tombstone as a miss, and a host refuses a fill of any older version, so a slow read can't
/// put back a value that a later change replaced.
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
    /// When unset there is no service cache: a host fills nothing and a client always asks the service.
    /// </remarks>
    public const string ConnectionKey = "Caching:Owned:Redis:Configuration";

    /// <summary>
    /// The section a resource's switch lives in: <c>Caching:Owned:Resources:{service}:{type}:Enabled</c>.
    /// </summary>
    /// <remarks>
    /// <c>false</c> tells the owner's host not to fill that resource and its clients not to read it,
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
    /// The field holding the format a resource's entry is written in.
    /// </summary>
    public const string Format = "f";

    /// <summary>
    /// The format of a resource's entry this release writes and reads: the whole resource, less the attributes worked
    /// out per caller. Published as <c>format</c> in <c>x-bitwarden-cache</c>.
    /// </summary>
    public const int CurrentFormat = 1;

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
    /// The placeholder a client fills with the organization its call carries: the owner of a key within its owner, or
    /// the key of a read of <c>current</c>.
    /// </summary>
    public const string OrganizationPlaceholder = "{organization}";

    /// <summary>
    /// The placeholder a client fills with the person its call carries.
    /// </summary>
    public const string UserPlaceholder = "{user}";

    /// <summary>
    /// The placeholder a client fills with the provider its call carries.
    /// </summary>
    public const string ProviderPlaceholder = "{provider}";

    // Segments kept for a service's other keys under the same prefix (idempotency records, and the index, generation
    // and answer segments of cached list reads and answers): a resource type taking one would mix its entries with
    // them in a scan of that segment.
    private static readonly string[] ReservedResourceTypes = ["answer", "index", "gen", "idempotency"];

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
    /// The key template of a resource's entries under a key other than its identifier, which a table backing several
    /// cacheable reads keeps apart from its entries by identifier: <c>{service}:{type}:by-{key name}:{key}</c>, or,
    /// for a key that names a row only within its owner, <c>{service}:{type}:by-{key name}:{owner}:{key}</c>, the
    /// owner's placeholder (<c>{organization}</c>, <c>{user}</c> or <c>{provider}</c>) naming its kind.
    /// </summary>
    /// <param name="service">The owning service's name.</param>
    /// <param name="resourceType">The resource's type.</param>
    /// <param name="keyName">What a caller calls the key: <c>user</c>, <c>organization</c>.</param>
    /// <param name="ownerAxis">For a key within its owner, the owner's kind (<c>organization</c>, <c>user</c> or <c>provider</c>); otherwise null.</param>
    /// <exception cref="ArgumentException">A name is empty, contains <c>:</c>, <c>{</c> or <c>}</c>, or is reserved; or the owner's kind is none of the three.</exception>
    public static string KeyedTemplate(string service, string resourceType, string keyName, string? ownerAxis)
    {
        ThrowIfNotAName(keyName);
        var resource = Template(service, resourceType)[..^KeyPlaceholder.Length];

        if (ownerAxis is null)
        {
            return $"{resource}by-{keyName}:{KeyPlaceholder}";
        }

        return ownerAxis is "organization" or "user" or "provider"
            ? $"{resource}by-{keyName}:{{{ownerAxis}}}:{KeyPlaceholder}"
            : throw new ArgumentException("An owner is an organization, a user or a provider.", nameof(ownerAxis));
    }

    /// <summary>
    /// The key template of a cached read-many: every row of a resource under the party a call carries, as one entry.
    /// <c>{service}:{type}:read-many:{party}</c>, the party's placeholder (<c>{organization}</c>, <c>{user}</c> or
    /// <c>{provider}</c>) standing for the key. A read-many's resource is its own, so its type alone keeps its entries
    /// apart from every other read's.
    /// </summary>
    /// <param name="service">The owning service's name.</param>
    /// <param name="resourceType">The resource's type.</param>
    /// <param name="partyAxis">The party the key is: <c>organization</c>, <c>user</c> or <c>provider</c>.</param>
    /// <exception cref="ArgumentException">
    /// A name is empty, contains <c>:</c>, <c>{</c> or <c>}</c>, or is reserved; or the party is none of the three.
    /// </exception>
    public static string ReadManyTemplate(string service, string resourceType, string partyAxis)
    {
        if (partyAxis is not ("organization" or "user" or "provider"))
        {
            throw new ArgumentException("A party is an organization, a user or a provider.", nameof(partyAxis));
        }

        var resource = Template(service, resourceType)[..^KeyPlaceholder.Length];

        return $"{resource}read-many:{{{partyAxis}}}";
    }

    /// <summary>
    /// The key of one entry.
    /// </summary>
    /// <param name="template">The resource's key template.</param>
    /// <param name="key">The identifier or key.</param>
    public static string Key(string template, Guid key) => template.Replace(KeyPlaceholder, key.ToString("D"), StringComparison.Ordinal);

    /// <summary>
    /// The key of one entry from a template whose placeholders name the key and the parties a call carries, or null
    /// when the template names one that isn't given.
    /// </summary>
    /// <param name="template">The key template.</param>
    /// <param name="key">The identifier or key, for <c>{key}</c>.</param>
    /// <param name="organization">For <c>{organization}</c>.</param>
    /// <param name="user">For <c>{user}</c>.</param>
    /// <param name="provider">For <c>{provider}</c>.</param>
    /// <returns>The key, or null when a placeholder is left unfilled.</returns>
    public static string? Key(string template, Guid? key, Guid? organization, Guid? user, Guid? provider)
    {
        ArgumentNullException.ThrowIfNull(template);

        var filled = template;

        foreach (var (placeholder, value) in new[]
        {
            (KeyPlaceholder, key), (OrganizationPlaceholder, organization), (UserPlaceholder, user), (ProviderPlaceholder, provider),
        })
        {
            if (filled.Contains(placeholder, StringComparison.Ordinal))
            {
                if (value is not { } given || given == Guid.Empty)
                {
                    return null;
                }

                filled = filled.Replace(placeholder, given.ToString("D"), StringComparison.Ordinal);
            }
        }

        return filled.Contains('{', StringComparison.Ordinal) ? null : filled;
    }

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
