using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;
using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Rules;

/// <summary>
/// Reads named arguments off a <c>[RestrictedDependency]</c> or <c>[RestrictedDependencyException]</c>
/// attribute. Shared by <see cref="RestrictedTypeModel"/> and <see cref="ExceptionGrantModel"/> so
/// the two readers cannot drift apart.
/// </summary>
internal static class AttributeDataExtensions
{
    /// <summary>
    /// The value of the named string argument, or null when it is absent, is not a string, or is
    /// blank. Blank counts as absent so a half-written attribute fails validation rather than
    /// passing with an empty owner or reason.
    /// </summary>
    public static string? ReadString(this AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is string value && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// The value of the named boolean argument, or <paramref name="fallback"/> when it is absent.
    /// </summary>
    public static bool ReadBool(this AttributeData attribute, string name, bool fallback)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is bool value)
            {
                return value;
            }
        }

        return fallback;
    }

    /// <summary>
    /// The elements of the named string-array argument, empty when it is absent. An element that
    /// is not a string comes back as null for the caller to reject.
    /// </summary>
    public static IEnumerable<string?> ReadStringArray(this AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Kind == TypedConstantKind.Array)
            {
                return argument.Value.Values.Select(v => v.Value as string);
            }
        }

        return [];
    }

    /// <summary>
    /// Where to report a problem with the attribute: the attribute syntax itself, falling back to
    /// the symbol it was applied to when the syntax is unavailable.
    /// </summary>
    public static Location LocationOf(this AttributeData attribute, ISymbol fallback) =>
        attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? SymbolFacts.SourceLocation(fallback);
}
