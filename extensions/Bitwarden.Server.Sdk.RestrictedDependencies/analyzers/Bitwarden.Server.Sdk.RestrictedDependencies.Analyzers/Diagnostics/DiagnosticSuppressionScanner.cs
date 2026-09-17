using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Diagnostics;

/// <summary>
/// BW0012. Finds the two ways a restricted-dependency diagnostic can be silenced in source: a
/// <c>[SuppressMessage]</c> naming one of the family's ids, and a <c>#pragma warning disable</c>.
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
    /// Pragma directives in one tree. A bare <c>disable</c> with no ids silences everything, which
    /// includes BW0011, so it is reported under that description.
    /// </summary>
    public static IEnumerable<Diagnostic> OnSyntaxTree(SyntaxTree tree, string? repoRoot, CancellationToken cancellationToken)
    {
        var root = tree.GetRoot(cancellationToken);
        foreach (var directive in root.DescendantTrivia().Select(t => t.GetStructure()).OfType<PragmaWarningDirectiveTriviaSyntax>())
        {
            if (!directive.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword))
            {
                continue;
            }

            var ids = directive.ErrorCodes.Select(c => c.ToString()).Where(DiagnosticDescriptors.Ids.Contains).ToList();
            if (ids.Count == 0 && directive.ErrorCodes.Count == 0)
            {
                ids.Add($"every warning, including {DiagnosticDescriptors.ExceptionExpired.Id}");
            }

            foreach (var id in ids)
            {
                yield return Diagnostic.Create(
                    DiagnosticDescriptors.UnstructuredSuppression, Location.None, $"#pragma warning disable at {Describe(directive.GetLocation(), repoRoot)}", id);
            }
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
