using System.Linq.Expressions;
using System.Reflection;
using Bitwarden.Server.Sdk.Data;
using Bitwarden.Server.Sdk.Resources.Querying;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// A declared cacheable resource.
/// </summary>
internal sealed class CacheableResource<TEntity, TResource> : ICacheableResource
    where TEntity : class
    where TResource : Resource
{
    public CacheableResource(Type query, CacheableQueryBuilder<TEntity, TResource> declared)
    {
        Query = query;
        Read = new StandardReadDeclaration<TEntity, TResource>(declared.Read);
        var read = Read;
        Lifetime = declared.CachedFor;
        WhenUnreachable = declared.WhenUnreachable;
        Cascades = [.. declared.CascadeDeclarations];
        ResourceType = ResourceTypes.For(typeof(TResource));

        var properties = typeof(TResource)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(StoredFields.Published, StringComparer.Ordinal);

        foreach (var field in declared.PerCallerAttributes)
        {
            if (!Fields<TResource>.Attributes.Contains(field) || !properties.ContainsKey(field))
            {
                throw new InvalidOperationException(
                    $"{query.Name} declares '{field}' per caller, which is not one of {typeof(TResource).Name}'s attributes.");
            }
        }

        PerCaller = declared.PerCallerAttributes.ToHashSet(StringComparer.Ordinal);
        Fields = [.. Fields<TResource>.Attributes.Where(attribute => !PerCaller.Contains(attribute)).Order(StringComparer.Ordinal)];

        // Read across owners, an entry answers any holder of the scope, whatever owner the call names: no personal
        // data may be among what it holds.
        if (read.AcrossOwners
            && Fields.FirstOrDefault(field => properties.TryGetValue(field, out var property)
                && property.IsDefined(typeof(PersonalDataAttribute), inherit: true)) is { } personal)
        {
            throw new InvalidOperationException(
                $"{typeof(TResource).Name}.{properties[personal].Name} is personal data ([PersonalData]), and "
                + $"{typeof(TResource).Name} is read across owners, so a cached copy would answer any holder of the scope "
                + "about anybody. Work it out per caller (PerCaller), or read it within its owner.");
        }

        if (read.Ownership == ReadOwnership.Rows && read.Owner is null)
        {
            throw new InvalidOperationException(
                $"{typeof(TEntity).Name} has several possible owners. A cached entry is checked against one, so it cannot be cached.");
        }

        var version = typeof(TEntity).GetProperty(declared.VersionProperty);

        if (version is null || (version.PropertyType != typeof(int) && version.PropertyType != typeof(long)))
        {
            throw new InvalidOperationException(
                $"{typeof(TEntity).Name} has no {declared.VersionProperty}. A cached entry is the row at a version; without "
                + "one a slow read could put an old value back after the change that replaced it.");
        }

        _version = version;
        KeyProperty = read.Key.Body is MemberExpression { Member: PropertyInfo keyed }
            ? keyed.Name
            : throw new InvalidOperationException(
                $"{typeof(TResource).Name} is keyed by {read.Key.Body}, which is not a property of {typeof(TEntity).Name}.");
        CurrentFrom = declared.Current;
        KeyColumns = read.WithinOwner ? [KeyProperty, read.OwnerProperty!.PropertyName] : [KeyProperty];

        if (CurrentFrom is not null && ById)
        {
            throw new InvalidOperationException(
                $"{query.Name} reads {typeof(TResource).Name} by identifier, which a context doesn't name: only a read by a "
                + "key (KeyedBy) may be read as current (CurrentFrom).");
        }
    }

    public Owner? CurrentFrom { get; }

    public IReadOnlyList<string> KeyColumns { get; }

    public bool WithinOwner => Read.WithinOwner;

    public bool NotUnique => Read.NotUnique;

    private readonly PropertyInfo _version;

    public string KeyProperty { get; }

    public bool KeyIsOwner => !ById && Read.OwnerProperty is { Through: null } owned && owned.PropertyName == KeyProperty;

    public StandardReadDeclaration<TEntity, TResource> Read { get; }

    public bool AcrossOwners => Read.AcrossOwners;

    public string ResourceType { get; }

    public Type Resource => typeof(TResource);

    public Type Entity => typeof(TEntity);

    public bool ById => Read.ById;

    public string KeyName => Read.KeyName;

    public IReadOnlyList<string> Fields { get; }

    public IReadOnlySet<string> PerCaller { get; }

    public Type Query { get; }

    public TimeSpan Lifetime { get; }

    public CacheWrites WhenUnreachable { get; }

    public IReadOnlyList<CascadeDeclaration> Cascades { get; }

    public ReadOwnership Ownership => Read.Ownership;

    public Owner? Owner => Read.Owner;

    public string VersionProperty => _version.Name;

    public string Template(string service) => ById
        ? OwnedCacheEntry.Template(service, ResourceType)
        : OwnedCacheEntry.KeyedTemplate(service, ResourceType, KeyName, WithinOwner ? CacheFill.Axis(Owner!.Value) : null);

    public string PublishedTemplate(string service, bool current) => current && CurrentFrom is { } axis
        ? Template(service).Replace(OwnedCacheEntry.KeyPlaceholder, "{" + CacheFill.Axis(axis) + "}", StringComparison.Ordinal)
        : Template(service);

    public string? EntryKey(string service, Func<string, object?> column) =>
        column(KeyProperty) is Guid key
            ? EntryKey(service, key, WithinOwner ? column(KeyColumns[1]) as Guid? : null)
            : null;

    /// <summary>
    /// The entry key of a read by this key, within this owner where the key is within its owner.
    /// </summary>
    public string? EntryKey(string service, Guid key, Guid? owner)
    {
        if (WithinOwner && owner is null)
        {
            return null;
        }

        var template = Template(service);

        return OwnedCacheEntry.Key(
            template,
            key,
            Owner == Data.Owner.Organization ? owner : null,
            Owner == Data.Owner.User ? owner : null,
            Owner == Data.Owner.Provider ? owner : null);
    }

    public long VersionOf(TEntity entity) => Convert.ToInt64(_version.GetValue(entity), System.Globalization.CultureInfo.InvariantCulture);

    public IReadOnlyList<Microsoft.EntityFrameworkCore.Metadata.INavigation> Included(Microsoft.EntityFrameworkCore.DbContext context) =>
        IncludedRows.Of(
            Read.Shape(context.Set<TEntity>()),
            context.Model.FindEntityType(typeof(TEntity))
                ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not in the model its cached read queries."),
            typeof(TResource).Name,
            KeyProperty);

    public (string? Current, string? Original) EntryKeysOf(string service, Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry) =>
        (EntryKey(service, column => entry.Property(column).CurrentValue),
            EntryKey(service, column => entry.Property(column).OriginalValue));
}
