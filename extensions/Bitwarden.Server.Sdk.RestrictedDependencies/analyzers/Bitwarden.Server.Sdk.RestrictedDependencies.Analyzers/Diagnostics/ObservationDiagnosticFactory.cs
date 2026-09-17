using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Diagnostics;

/// <summary>
/// Builds the BW0017 rows. Everything a repository's baseline tool needs that no enforcement
/// diagnostic already carries leaves through here: the compliant uses, the declared member set of
/// each sealed type, every exception, and each type's owner. Compliant code produces no diagnostic by
/// definition, so without this channel the tool would need a second scanner.
/// </summary>
internal static class ObservationDiagnosticFactory
{
    /// <summary>
    /// One use of a restricted type, aggregated per site.
    /// </summary>
    public static Diagnostic Usage(
        string type,
        DependencyUsageType kind,
        string? member,
        string site,
        int count,
        string file,
        bool excepted,
        bool tracked)
    {
        var properties = Row(ObservationConstants.UsageRow, type)
            .Add(ObservationConstants.UsageKind, kind.ToToken())
            .Add(ObservationConstants.Member, member)
            .Add(ObservationConstants.Site, site)
            .Add(ObservationConstants.Count, count.ToString(CultureInfo.InvariantCulture))
            .Add(ObservationConstants.File, file)
            .Add(ObservationConstants.Excepted, ObservationConstants.ToToken(excepted))
            .Add(ObservationConstants.Tracked, ObservationConstants.ToToken(tracked));

        return Create(properties, $"{type}: {kind.ToToken()} x{count} at {site}");
    }

    /// <summary>
    /// One member the seal on a restricted type covers.
    /// </summary>
    public static Diagnostic DeclaredMember(string type, string member) =>
        Create(
            Row(ObservationConstants.DeclaredMemberRow, type).Add(ObservationConstants.Member, member),
            $"{type}: declares {member}");

    /// <summary>
    /// One <c>[RestrictedDependencyException]</c>, valid or not, for the debt report.
    /// </summary>
    public static Diagnostic ExceptionUsage(
        string type,
        string target,
        string? owner,
        string? reason,
        string? expires,
        bool valid,
        bool expired)
    {
        var properties = Row(ObservationConstants.ExceptionRow, type)
            .Add(ObservationConstants.Target, target)
            .Add(ObservationConstants.Owner, owner)
            .Add(ObservationConstants.Reason, reason)
            .Add(ObservationConstants.Expires, expires)
            .Add(ObservationConstants.Valid, ObservationConstants.ToToken(valid))
            .Add(ObservationConstants.Expired, ObservationConstants.ToToken(expired));

        return Create(properties, $"{type}: excepted at {target}");
    }

    /// <summary>
    /// The owner and tracking id of one restricted type, so the report can attribute debt without
    /// re-reading the attribute.
    /// </summary>
    public static Diagnostic RestrictedType(string type, string owner, string tracking) =>
        Create(
            Row(ObservationConstants.RestrictedTypeRow, type)
                .Add(ObservationConstants.Owner, owner)
                .Add(ObservationConstants.Tracking, tracking),
            $"{type}: owned by {owner} ({tracking})");

    private static ImmutableDictionary<string, string?> Row(string row, string type) =>
        ImmutableDictionary<string, string?>.Empty
            .Add(ObservationConstants.Row, row)
            .Add(ObservationConstants.Type, type);

    private static Diagnostic Create(ImmutableDictionary<string, string?> properties, string message) =>
        Diagnostic.Create(DiagnosticDescriptors.Observation, Location.None, properties, message);
}
