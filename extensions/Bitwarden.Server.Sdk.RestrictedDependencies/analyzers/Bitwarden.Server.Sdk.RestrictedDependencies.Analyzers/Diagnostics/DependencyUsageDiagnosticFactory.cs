using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Rules;
using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Diagnostics;

/// <summary>
/// Builds the BW0005-BW0009 diagnostics. One place decides how a use is described, so the five
/// usage kinds cannot drift in wording or in which argument carries what.
/// </summary>
internal static class DependencyUsageDiagnosticFactory
{
    /// <summary>
    /// The diagnostic for one use, chosen by its usage kind. Tracking and owner fall back from
    /// the member's own attribute to the type's, so a member override need only say what differs.
    /// </summary>
    public static Diagnostic Create(
        DependencyUsageType kind,
        RestrictedTypeModel model,
        UseRule useRule,
        ISymbol? member,
        ISymbol subject,
        Location location)
    {
        var typeName = model.Type.Name;
        var tracking = useRule.Tracking ?? model.Tracking;
        var owner = useRule.Owner ?? model.Owner;
        return kind switch
        {
            DependencyUsageType.Injection => Diagnostic.Create(DiagnosticDescriptors.Injection, location, ContainingTypeName(subject), typeName, tracking, owner),
            DependencyUsageType.Member => Diagnostic.Create(DiagnosticDescriptors.MemberUse, location, member?.Name ?? subject.Name, typeName, tracking, MemberHint(useRule), owner),
            DependencyUsageType.Locator => Diagnostic.Create(DiagnosticDescriptors.Locator, location, typeName, tracking, owner),
            DependencyUsageType.Concrete => Diagnostic.Create(DiagnosticDescriptors.Concrete, location, subject.Name, typeName, tracking, owner),
            DependencyUsageType.Escape => Diagnostic.Create(DiagnosticDescriptors.Escape, location, typeName, tracking, subject.Name, owner),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static string ContainingTypeName(ISymbol subject) =>
        subject.ContainingType?.Name ?? subject.ContainingSymbol?.Name ?? subject.Name;

    private static string MemberHint(UseRule useRule)
    {
        if (useRule.IsForbidden)
        {
            return useRule.Replacement is null ? "every use is forbidden" : $"every use is forbidden, use {useRule.Replacement} instead";
        }

        return useRule.Replacement is null
            ? "this use exceeds the baseline and new uses are not allowed"
            : $"this use exceeds the baseline, use {useRule.Replacement} instead";
    }
}
