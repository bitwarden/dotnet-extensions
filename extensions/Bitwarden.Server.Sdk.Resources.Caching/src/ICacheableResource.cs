using Bitwarden.Server.Sdk.Data;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// A declared cacheable resource, as the cache's parts see it.
/// </summary>
internal interface ICacheableResource
{
    /// <summary>
    /// The resource's type, as its documents name it.
    /// </summary>
    string ResourceType { get; }

    /// <summary>
    /// The resource.
    /// </summary>
    Type Resource { get; }

    /// <summary>
    /// The row.
    /// </summary>
    Type Entity { get; }

    /// <summary>
    /// Whether it is read by identifier; otherwise by <see cref="KeyName"/>.
    /// </summary>
    bool ById { get; }

    /// <summary>
    /// What a caller calls the key: <c>id</c>, or a filter's name.
    /// </summary>
    string KeyName { get; }

    /// <summary>
    /// The attributes an entry holds: every one but <see cref="PerCaller"/>.
    /// </summary>
    IReadOnlyList<string> Fields { get; }

    /// <summary>
    /// The attributes worked out on every read and never cached.
    /// </summary>
    IReadOnlySet<string> PerCaller { get; }

    /// <summary>
    /// The cacheable query that declares it.
    /// </summary>
    Type Query { get; }

    /// <summary>
    /// How long an entry, or a tombstone, lives.
    /// </summary>
    TimeSpan Lifetime { get; }

    /// <summary>
    /// What a change does when the cache can't be told of it before it commits.
    /// </summary>
    CacheWrites WhenUnreachable { get; }

    /// <summary>
    /// The database cascades the read declares, through foreign keys of the tables it is built from.
    /// </summary>
    IReadOnlyList<CascadeDeclaration> Cascades { get; }

    /// <summary>
    /// How the rows are owned.
    /// </summary>
    ReadOwnership Ownership { get; }

    /// <summary>
    /// The owner kind, for owned rows.
    /// </summary>
    Owner? Owner { get; }

    /// <summary>
    /// Whether the service reads it across owners, so a cached read checks the scope alone, as the
    /// service does, and not the entry's owner against the call's.
    /// </summary>
    bool AcrossOwners { get; }

    /// <summary>
    /// The key template entries are stored under, once the service's name is known: <c>{key}</c> for the identifier
    /// or key, and the owner's placeholder for a key within its owner.
    /// </summary>
    string Template(string service);

    /// <summary>
    /// The key template an operation publishes: the stored one, with the party the context names in place of
    /// <c>{key}</c> for a read of <c>current</c>.
    /// </summary>
    string PublishedTemplate(string service, bool current);

    /// <summary>
    /// An entry's key from the row's columns, or null when the row has no key (a membership nobody has accepted).
    /// </summary>
    /// <param name="service">The service's name.</param>
    /// <param name="column">A column's value, by property name.</param>
    string? EntryKey(string service, Func<string, object?> column);

    /// <summary>
    /// The columns an entry's key is made of: the key's, and the owner's for a key within its owner.
    /// </summary>
    IReadOnlyList<string> KeyColumns { get; }

    /// <summary>
    /// Whether the key names a row only within its owner.
    /// </summary>
    bool WithinOwner { get; }

    /// <summary>
    /// Whether several rows may hold one key.
    /// </summary>
    bool NotUnique { get; }

    /// <summary>
    /// The party whose identifier is the key of a read of <c>current</c>; null where there is none.
    /// </summary>
    Owner? CurrentFrom { get; }

    /// <summary>
    /// The navigations its read includes, whose rows its entry is built from too (<see cref="IncludedRows"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">An include the cache can't invalidate.</exception>
    IReadOnlyList<Microsoft.EntityFrameworkCore.Metadata.INavigation> Included(Microsoft.EntityFrameworkCore.DbContext context);

    /// <summary>
    /// The entry key of a row as it now stands, and as it stood when read; equal unless the key changed, null where
    /// the row has none.
    /// </summary>
    (string? Current, string? Original) EntryKeysOf(string service, Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry);

    /// <summary>
    /// The name of the row's version property.
    /// </summary>
    string VersionProperty { get; }

    /// <summary>
    /// The name of the property the cache is keyed by: <c>Id</c>, or the unique key's.
    /// </summary>
    string KeyProperty { get; }

    /// <summary>
    /// Whether the unique key is the row's owner, as an organization's entitlements are keyed by it.
    /// </summary>
    bool KeyIsOwner { get; }
}
