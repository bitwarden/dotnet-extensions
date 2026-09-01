using System.ComponentModel;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bitwarden.Server.Sdk.Database;

/// <summary>
/// Base <see cref="IMigrationsAssembly"/> implementation for source-generated migrations-assembly
/// dispatchers. Stores pre-built migration data emitted by the generator, so there's no
/// runtime type scanning or <c>Activator.CreateInstance</c> involved.
/// </summary>
/// <remarks>
/// This type is meant for the <c>DatabaseSetupGenerator</c> source generator. Prefer applying
/// <c>[assembly: DatabaseSetup&lt;TContext&gt;]</c> over referencing it directly.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class MigrationsAssemblyBase : IMigrationsAssembly
{
    private readonly Dictionary<Type, Func<Migration>> _factories;

    /// <summary>
    /// Initialises the migrations-assembly with pre-built, provider-specific data.
    /// </summary>
    /// <param name="assembly">Assembly that contains the migration types.</param>
    /// <param name="entries">
    /// Migration entries for the active provider, each with the migration ID, its
    /// <see cref="TypeInfo"/>, and a factory that instantiates it without reflection.
    /// Emitted as a static array by the source generator.
    /// </param>
    /// <param name="modelSnapshot">
    /// Model snapshot for the active provider, or <see langword="null"/> if none exists.
    /// Emitted as <c>new TheSnapshot()</c> by the source generator.
    /// </param>
    protected MigrationsAssemblyBase(
        Assembly assembly,
        (string Id, TypeInfo Info, Func<Migration> Factory)[] entries,
        ModelSnapshot? modelSnapshot)
    {
        Assembly = assembly;
        ModelSnapshot = modelSnapshot;

        var migrations = new Dictionary<string, TypeInfo>(entries.Length, StringComparer.Ordinal);
        _factories = new Dictionary<Type, Func<Migration>>(entries.Length);

        foreach (var (id, info, factory) in entries)
        {
            migrations[id] = info;
            _factories[info.AsType()] = factory;
        }

        Migrations = migrations;
    }

    /// <inheritdoc />
    public Assembly Assembly { get; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, TypeInfo> Migrations { get; }

    /// <inheritdoc />
    public ModelSnapshot? ModelSnapshot { get; }

    /// <inheritdoc />
    public string? FindMigrationId(string nameOrId)
    {
        if (Migrations.ContainsKey(nameOrId))
            return nameOrId;

        var matches = Migrations.Keys
            .Where(id => id.EndsWith(nameOrId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException($"The migration name '{nameOrId}' is ambiguous."),
        };
    }

    /// <inheritdoc />
    public Migration CreateMigration(TypeInfo migrationClass, string activeProvider)
    {
        if (!_factories.TryGetValue(migrationClass.AsType(), out var factory))
            throw new InvalidOperationException(
                $"No factory registered for migration '{migrationClass.Name}'. " +
                $"Ensure the source generator has run and the migration type is in the expected namespace.");

        var migration = factory();
        migration.ActiveProvider = activeProvider;
        return migration;
    }
}
