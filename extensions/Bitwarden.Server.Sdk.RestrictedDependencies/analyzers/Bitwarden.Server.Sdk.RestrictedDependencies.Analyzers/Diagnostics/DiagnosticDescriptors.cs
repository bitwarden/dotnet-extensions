using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Diagnostics;

/// <summary>
/// The restricted-dependency diagnostic family. Every rule except the expiry warning and the
/// observation channel is an error that no configuration can lower; BW0016 reports any
/// configuration that tries.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string _category = "RestrictedDependencies";

    private const string _helpUrlFormat = "https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#{0}";

    public static readonly DiagnosticDescriptor Injection = Error(
        DiagnosticConstants.Injection,
        "Restricted dependency injected at a new site",
        "'{0}' injects restricted dependency '{1}' ({2}, owner: {3}); this site is not in its baseline and new uses are not allowed");

    public static readonly DiagnosticDescriptor MemberUse = Error(
        DiagnosticConstants.MemberUse,
        "Restricted member used beyond its budget",
        "'{0}' is a restricted member of '{1}' ({2}, owner: {4}); {3}");

    public static readonly DiagnosticDescriptor Locator = Error(
        DiagnosticConstants.Locator,
        "Restricted dependency resolved from the service locator",
        "'{0}' is a restricted dependency ({1}, owner: {2}) resolved through the service locator at a site that is not in its baseline");

    public static readonly DiagnosticDescriptor Concrete = Error(
        DiagnosticConstants.Concrete,
        "Implementation of a restricted dependency referenced directly",
        "'{0}' implements restricted dependency '{1}' ({2}, owner: {3}); this reference is not in its baseline");

    public static readonly DiagnosticDescriptor Escape = Error(
        DiagnosticConstants.Escape,
        "Restricted dependency escapes its consumer",
        "Restricted dependency '{0}' ({1}, owner: {3}) escapes through '{2}'; this site is not in its baseline");

    public static readonly DiagnosticDescriptor ExceptionInvalid = Error(
        DiagnosticConstants.ExceptionInvalid,
        "Restricted dependency exception is incomplete",
        "[RestrictedDependencyException] on '{0}' is invalid: {1}");

    public static readonly DiagnosticDescriptor ExceptionExpired = new(
        DiagnosticConstants.ExceptionExpired,
        "Restricted dependency exception has expired",
        "[RestrictedDependencyException] on '{0}' for '{1}' expired on {2} (owner: {3}, reason: {4}); fix the site or renew the date",
        _category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: string.Format(_helpUrlFormat, DiagnosticConstants.ExceptionExpired));

    public static readonly DiagnosticDescriptor UnstructuredSuppression = Error(
        DiagnosticConstants.UnstructuredSuppression,
        "Restricted dependency diagnostic suppressed without an exception",
        "{0} suppresses {1}; use [RestrictedDependencyException] with an owner, reason and expiry instead");

    public static readonly DiagnosticDescriptor StaleBaseline = Error(
        DiagnosticConstants.StaleBaseline,
        "Baseline entry no longer matches the code",
        "Baseline for '{0}' expects {1} {2} at '{3}' in project '{4}' but the code has {5}; {6}");

    public static readonly DiagnosticDescriptor SealedTypeGrew = Error(
        DiagnosticConstants.SealedTypeGrew,
        "Sealed type gained a member",
        "'{0}' is sealed ({1}) and '{2}' is not in its baseline; put new behavior in the owning team's own type");

    public static readonly DiagnosticDescriptor AttributeInvalid = Error(
        DiagnosticConstants.AttributeInvalid,
        "Restricted dependency attribute or baseline is invalid",
        "{0}");

    public static readonly DiagnosticDescriptor SeverityLowered = Error(
        DiagnosticConstants.SeverityLowered,
        "Restricted dependency diagnostic configured below error",
        "{0} is configured as '{1}' by {2}; restricted dependency diagnostics must remain errors");

    /// <summary>
    /// The observation channel. Disabled by default, so it emits nothing in a real build; a
    /// repository's baseline tool enables it through
    /// <c>CompilationOptions.WithSpecificDiagnosticOptions</c> and reads the rows back out of
    /// <see cref="Diagnostic.Properties"/>.
    /// </summary>
    public static readonly DiagnosticDescriptor Observation = new(
        ObservationConstants.DiagnosticId,
        "Restricted dependency observation",
        "{0}",
        _category,
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: false,
        helpLinkUri: string.Format(_helpUrlFormat, ObservationConstants.DiagnosticId));

    public static readonly ImmutableArray<DiagnosticDescriptor> All =
    [
        Injection, MemberUse, Locator, Concrete, Escape, ExceptionInvalid, ExceptionExpired,
        UnstructuredSuppression, StaleBaseline, SealedTypeGrew, AttributeInvalid, SeverityLowered,
        Observation,
    ];

    /// <summary>
    /// Every id in the family. The ids sit in a range shared with the rest of the package's
    /// diagnostics, so anything matching against the family must match this set rather than a
    /// prefix.
    /// </summary>
    public static readonly ImmutableHashSet<string> Ids = [.. All.Select(d => d.Id)];

    /// <summary>
    /// The ids that must stay errors. The expiry warning is a warning by design, and the
    /// observation channel is configured off and on by the tool that reads it.
    /// </summary>
    public static readonly ImmutableArray<DiagnosticDescriptor> MustRemainErrors =
        [.. All.Where(d => d.Id != ExceptionExpired.Id && d.Id != Observation.Id)];

    private static DiagnosticDescriptor Error(string id, string title, string messageFormat) =>
        new(id, title, messageFormat, _category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: string.Format(_helpUrlFormat, id), customTags: WellKnownDiagnosticTags.NotConfigurable);
}
