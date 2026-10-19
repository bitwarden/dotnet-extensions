using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;
using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Diagnostics;

/// <summary>
/// BW0012. Finds a <c>[SuppressMessage]</c> naming one of the family's ids. A
/// <c>#pragma warning disable</c> is not scanned: the error ids are not configurable, so a pragma
/// cannot silence them.
/// </summary>
/// <remarks>
/// The ids sit in a range shared with the rest of the package's diagnostics, so this matches the
/// family's id set exactly. A prefix match would claim a sibling package's id.
/// </remarks>
internal static class DiagnosticSuppressionScanner
{
    /// <summary>
    /// Suppression attributes on one symbol. Reported without a source location on purpose: a
    /// diagnostic placed inside the symbol would itself be silenced by the same attribute. The
    /// message carries the path and line instead.
    /// </summary>
    public static IEnumerable<Diagnostic> OnSymbol(ISymbol symbol, string? repoRoot)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;
            if (name is not ("SuppressMessageAttribute" or "UnconditionalSuppressMessageAttribute"))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length < 2 || attribute.ConstructorArguments[1].Value is not string checkId
                || !DiagnosticDescriptors.Ids.Contains(IdOf(checkId)))
            {
                continue;
            }

            var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? SymbolFacts.SourceLocation(symbol);
            yield return Diagnostic.Create(
                DiagnosticDescriptors.UnstructuredSuppression,
                Location.None,
                $"[{name.Replace("Attribute", string.Empty)}] on '{symbol.Name}' at {Describe(location, repoRoot)}",
                checkId);
        }
    }

    /// <summary>
    /// The id out of a <c>[SuppressMessage]</c> check id, which conventionally carries the rule's
    /// title after a colon.
    /// </summary>
    private static string IdOf(string checkId)
    {
        var colon = checkId.IndexOf(':');
        return colon < 0 ? checkId : checkId.Substring(0, colon);
    }

    private static string Describe(Location location, string? repoRoot)
    {
        if (location.SourceTree is null)
        {
            return "an unknown location";
        }

        var line = location.GetLineSpan().StartLinePosition.Line + 1;
        return $"'{RepositoryPathNormalizer.ToRelative(location.SourceTree.FilePath, repoRoot)}' line {line}";
    }
}
