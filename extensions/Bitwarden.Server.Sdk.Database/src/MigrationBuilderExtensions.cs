using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

namespace Bitwarden.Server.Sdk.Database;

/// <summary>
/// Runs raw SQL an EF Core migration can't express, kept in <c>.sql</c> files so an editor can
/// help with it rather than in a C# string literal.
/// </summary>
public static class MigrationBuilderExtensions
{
    private const string HelperScriptSegment = ".HelperScripts.";

    /// <summary>
    /// Executes a script from <c>Migrations/{Provider}/HelperScripts/</c>, resolved for the
    /// provider the migration is running against.
    /// </summary>
    /// <param name="migrationBuilder">The builder to add the SQL operation to.</param>
    /// <param name="fileName">File name of the script, e.g. <c>2026-08-19_00_Backfill.sql</c>.</param>
    /// <param name="scriptAssembly">
    /// Assembly holding the script; defaults to the caller's, which is the migration's own
    /// assembly when called from a migration.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// No script matches, or more than one does — both of which are otherwise only discoverable
    /// part-way through a migration run.
    /// </exception>
    public static OperationBuilder<SqlOperation> SqlFromHelperScript(
        this MigrationBuilder migrationBuilder,
        string fileName,
        Assembly? scriptAssembly = null)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var assembly = scriptAssembly ?? Assembly.GetCallingAssembly();
        var resource = ResolveHelperScript(
            assembly.GetManifestResourceNames(),
            migrationBuilder.ActiveProvider,
            fileName);

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);

        return migrationBuilder.Sql(reader.ReadToEnd());
    }

    /// <summary>
    /// Picks the helper script for the active provider, falling back to a provider-agnostic one of
    /// the same name.
    /// </summary>
    internal static string ResolveHelperScript(
        IReadOnlyCollection<string> resourceNames,
        string? activeProvider,
        string fileName)
    {
        // The active provider is its package name, e.g. Microsoft.EntityFrameworkCore.Sqlite, whose
        // last segment is the folder migrations for that provider live in.
        var provider = activeProvider?.Split('.').LastOrDefault();

        var candidates = resourceNames
            .Where(name => name.EndsWith($"{HelperScriptSegment}{fileName}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"No helper script named '{fileName}' is embedded in {ScriptsSeenIn(resourceNames)}. "
                + "Scripts belong in Migrations/{Provider}/HelperScripts/.");
        }

        if (candidates.Count == 1)
            return candidates[0];

        var forProvider = candidates
            .Where(name => provider is not null
                && name.Contains($".{provider}{HelperScriptSegment}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return forProvider switch
        {
            [var only] => only,
            [] => throw new InvalidOperationException(
                $"Several helper scripts are named '{fileName}' and none is under a folder for "
                + $"provider '{activeProvider}': {string.Join(", ", candidates)}."),
            _ => throw new InvalidOperationException(
                $"Several helper scripts named '{fileName}' match provider '{activeProvider}': "
                + string.Join(", ", forProvider) + "."),
        };
    }

    private static string ScriptsSeenIn(IReadOnlyCollection<string> resourceNames)
    {
        var seen = resourceNames
            .Where(name => name.Contains(HelperScriptSegment, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return seen.Count == 0
            ? "the migration assembly (it embeds no helper scripts at all)"
            : $"the migration assembly, which embeds: {string.Join(", ", seen)}";
    }
}
