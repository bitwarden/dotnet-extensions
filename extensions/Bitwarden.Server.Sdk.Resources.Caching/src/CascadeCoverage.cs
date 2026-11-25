namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Why a foreign key that lets the database delete or change a row a cached read is built from leaves no entry stale,
/// declared on the read with <see cref="CacheableQueryBuilder{TEntity,TResource}.CascadeCovered{TDependent}"/>.
/// </summary>
/// <remarks>
/// A row the database deletes or changes through <c>ON DELETE CASCADE</c> or <c>SET NULL</c> never passes through the
/// change tracker, so nothing invalidates the entries built from it.
/// </remarks>
public enum CascadeCoverage
{
    /// <summary>
    /// The key is the one an included navigation names its parent by, the cached row itself. Deleting the parent goes
    /// through the change tracker and invalidates its entry, so the included rows the database then deletes or changes
    /// are never read from it. Checked against the model: refused on any other key.
    /// </summary>
    ParentInvalidated = 1,

    /// <summary>
    /// Every change that deletes the key's principal loads these rows into the context first, so the change tracker
    /// deletes or changes them itself and their entries are invalidated. Only true where the principal is never deleted
    /// another way: not by a database cascade from a row it depends on, and not by an owner's erasure that leaves these
    /// rows behind. Not something the package can check: the declaration's reason names the code that does it.
    /// </summary>
    DependentsLoaded = 2,
}
