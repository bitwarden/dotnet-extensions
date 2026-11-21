using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Finds, as the host starts, the foreign keys through which the database deletes or changes rows a cached read is
/// built from, and refuses or warns about each the read doesn't declare (<see cref="CascadeCoverage"/>).
/// </summary>
/// <remarks>
/// <para>
/// Invalidation reads the change tracker, which never sees a row the database deletes through <c>ON DELETE CASCADE</c>
/// or changes through <c>SET NULL</c>. Such a key on the read's own table, or on one its read includes, would leave
/// entries readable until they expire. <c>ClientCascade</c> and <c>ClientSetNull</c> leave the database refusing the
/// delete instead, so they need nothing.
/// </para>
/// <para>
/// The model is what is read: a key or trigger the database has and the model doesn't isn't seen.
/// </para>
/// </remarks>
internal static class CacheCascadeCheck
{
    /// <summary>
    /// The environment a host under test runs in, where an undeclared cascade refuses the host as in <c>Development</c>.
    /// </summary>
    public const string TestingEnvironment = "Testing";

    /// <summary>
    /// Refuses the host, in <c>Development</c> and <c>Testing</c>, or warns once elsewhere, when a cached read is built
    /// from a row the database may delete or change through a foreign key the read doesn't declare.
    /// </summary>
    /// <param name="resources">The declared resources.</param>
    /// <param name="database">A context of the service's database, for its model.</param>
    /// <param name="environment">Whether to refuse or warn.</param>
    /// <param name="logger">Where the warning goes.</param>
    /// <returns>Whether every cascade is declared.</returns>
    /// <exception cref="InvalidOperationException">
    /// A declaration names no such key, or one it can't cover; or a cascade is undeclared in <c>Development</c> or
    /// <c>Testing</c>.
    /// </exception>
    public static bool Check(IEnumerable<ICacheableResource> resources, DbContext database, IHostEnvironment environment, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        var undeclared = Undeclared(resources, database);

        if (undeclared.Count == 0)
        {
            return true;
        }

        var list = string.Join("; ", undeclared);

        if (environment.IsDevelopment() || environment.IsEnvironment(TestingEnvironment))
        {
            throw new InvalidOperationException(
                $"The service cache is built from rows the database deletes or changes through a foreign key the change "
                + $"tracker never sees, so their entries would be read until they expire: {list}. Declare each on its read "
                + "with CascadeCovered, saying why no entry is left stale, or give the key another delete behaviour.");
        }

        logger.LogWarning(
            "The service cache is built from rows the database deletes or changes through a foreign key no cacheable read "
            + "declares, so their entries may be read until they expire: {Cascades}.",
            list);
        return false;
    }

    /// <summary>
    /// Every foreign key a read is built from rows of that lets the database delete or change them, and that the read
    /// doesn't declare, described for a person.
    /// </summary>
    /// <exception cref="InvalidOperationException">A declaration names no such key, or one it can't cover.</exception>
    internal static IReadOnlyList<string> Undeclared(IEnumerable<ICacheableResource> resources, DbContext database)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(database);

        var undeclared = new List<string>();

        foreach (var resource in resources.OrderBy(resource => resource.Query.Name, StringComparer.Ordinal))
        {
            var root = database.Model.FindEntityType(resource.Entity)!;
            var included = resource.Included(database);
            var tables = included.Select(navigation => navigation.TargetEntityType).Prepend(root).Distinct().ToList();
            var cascades = tables
                .SelectMany(table => table.GetForeignKeys()
                    .Where(key => key.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.SetNull)
                    .Select(key => (Table: table, Key: key)))
                .ToList();
            var covered = new HashSet<IForeignKey>();

            foreach (var declaration in resource.Cascades)
            {
                var matched = cascades
                    .Where(cascade => cascade.Table.ClrType == declaration.Dependent
                        && cascade.Key.Properties.Select(property => property.Name).SequenceEqual(declaration.Properties, StringComparer.Ordinal))
                    .Select(cascade => cascade.Key)
                    .ToList();

                if (matched.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"{resource.Query.Name} declares a cascade through {Name(declaration.Dependent, declaration.Properties)}, "
                        + "which is no foreign key with a Cascade or SetNull delete behaviour on a table its read is built "
                        + "from. Remove the declaration, or name the key the model has.");
                }

                if (declaration.Coverage == CascadeCoverage.ParentInvalidated
                    && matched.Any(key => !included.Any(navigation => navigation.ForeignKey == key)))
                {
                    throw new InvalidOperationException(
                        $"{resource.Query.Name} declares {Name(declaration.Dependent, declaration.Properties)} covered as "
                        + $"{nameof(CascadeCoverage.ParentInvalidated)}, but it is not the key an included navigation names "
                        + $"{root.ClrType.Name} by: deleting its principal invalidates no entry these rows are read from.");
                }

                covered.UnionWith(matched);
            }

            undeclared.AddRange(cascades
                .Where(cascade => !covered.Contains(cascade.Key))
                .Select(cascade => $"{resource.Query.Name} reads {Name(cascade.Table.ClrType, [.. cascade.Key.Properties.Select(property => property.Name)])} "
                    + $"({cascade.Key.DeleteBehavior} from {cascade.Key.PrincipalEntityType.ClrType.Name})"));
        }

        return undeclared;
    }

    private static string Name(Type table, IReadOnlyList<string> properties) =>
        properties is [var one] ? $"{table.Name}.{one}" : $"{table.Name}({string.Join(", ", properties)})";
}
