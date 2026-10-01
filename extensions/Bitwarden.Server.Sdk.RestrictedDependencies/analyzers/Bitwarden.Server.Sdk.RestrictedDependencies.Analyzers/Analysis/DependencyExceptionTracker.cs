using System.Collections.Concurrent;
using System.Collections.Immutable;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Diagnostics;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Rules;
using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;

/// <summary>
/// Discovers <c>[RestrictedDependencyException]</c> attributes, validates them (BW0010/BW0011),
/// and answers whether a given use is covered by one. Reads are cached because the usage kinds
/// ask the same symbols repeatedly, and every exception seen is kept for the observation channel.
/// </summary>
internal sealed class DependencyExceptionTracker
{
    private readonly ConcurrentDictionary<ISymbol, ImmutableArray<ExceptionGrantModel>> _cache =
        new(SymbolEqualityComparer.Default);

    private readonly ConcurrentBag<Seen> _observed = [];
    private readonly DateTime _today;

    public DependencyExceptionTracker(DateTime today)
    {
        _today = today;
    }

    /// <summary>
    /// Every exception attribute seen, valid or not, as a BW0017 row in type then target order.
    /// </summary>
    public IEnumerable<Diagnostic> Observations() =>
        _observed
            .OrderBy(e => e.Type, StringComparer.Ordinal)
            .ThenBy(e => e.Target, StringComparer.Ordinal)
            .Select(e => ObservationDiagnosticFactory.ExceptionUsage(e.Type, e.Target, e.Owner, e.Reason, e.Expires, e.Valid, e.Expired));

    /// <summary>
    /// True when a valid exception covers a use of <paramref name="restrictedType"/> at this site.
    /// A member use looks only at its own member; every other kind walks the containing types,
    /// because a class-level exception covers the whole class.
    /// </summary>
    public bool Covers(DependencyUsageType kind, INamedTypeSymbol restrictedType, ISymbol containing)
    {
        if (kind == DependencyUsageType.Member)
        {
            return On(SymbolFacts.NearestMember(containing)).Any(e => e.Covers(restrictedType));
        }

        for (var type = containing as INamedTypeSymbol ?? containing.ContainingType; type is not null; type = type.ContainingType)
        {
            if (On(type).Any(e => e.Covers(restrictedType)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Records every exception on <paramref name="symbol"/> for the observation channel and
    /// returns the diagnostics they earn: BW0010 when required properties are missing, and BW0011
    /// when a valid one has lapsed. An invalid exception excepts nothing, so a half-written
    /// attribute fails closed.
    /// </summary>
    public IEnumerable<Diagnostic> Validate(ISymbol symbol, bool reportExpiry)
    {
        foreach (var exception in On(symbol))
        {
            var typeName = exception.RestrictedType is null ? "?" : SymbolFacts.MetadataName(exception.RestrictedType);
            var expired = exception.IsExpired(_today);
            _observed.Add(new Seen(typeName, SymbolFacts.SiteId(symbol), exception.Owner, exception.Reason, exception.Expires, exception.IsValid, expired));

            if (!exception.IsValid)
            {
                yield return Diagnostic.Create(
                    DiagnosticDescriptors.ExceptionInvalid, exception.Location, symbol.Name, string.Join("; ", exception.Problems));
            }
            else if (expired && reportExpiry)
            {
                yield return Diagnostic.Create(
                    DiagnosticDescriptors.ExceptionExpired, exception.Location, symbol.Name, exception.RestrictedType!.Name, exception.Expires, exception.Owner, exception.Reason);
            }
        }
    }

    private ImmutableArray<ExceptionGrantModel> On(ISymbol symbol) =>
        _cache.GetOrAdd(symbol, static s => ExceptionGrantModel.ReadAll(s));

    private sealed record Seen(string Type, string Target, string? Owner, string? Reason, string? Expires, bool Valid, bool Expired);
}
