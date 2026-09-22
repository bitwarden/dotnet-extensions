using System.Collections.Concurrent;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Diagnostics;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Rules;
using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis.Usage;

/// <summary>
/// Every use the callbacks observed, aggregated per usage key and split into the ones that count
/// against a baseline and the ones a valid exception covers. Written from concurrent callbacks, so
/// the maps are concurrent and each record guards its own locations.
/// </summary>
internal sealed class DependencyUsageLedger
{
    private readonly ConcurrentDictionary<DependencyUsageKey, DependencyUsageRecord> _counted = new();
    private readonly ConcurrentDictionary<DependencyUsageKey, DependencyUsageRecord> _excepted = new();

    /// <summary>
    /// Records one use that counts against the baseline.
    /// </summary>
    public void Add(DependencyUsageKey key, RestrictedTypeModel model, ISymbol? member, ISymbol subject, string file, Location location) =>
        _counted.GetOrAdd(key, _ => new DependencyUsageRecord(model, member, subject, file)).Add(location);

    /// <summary>
    /// Records one use that a valid exception covers. These are never written to a baseline.
    /// </summary>
    public void AddExcepted(DependencyUsageKey key, RestrictedTypeModel model, ISymbol? member, ISymbol subject, string file, Location location) =>
        _excepted.GetOrAdd(key, _ => new DependencyUsageRecord(model, member, subject, file)).Add(location);

    /// <summary>
    /// Counted uses in key order, so compilation-end diagnostics come out the same way on every run.
    /// </summary>
    public IEnumerable<(DependencyUsageKey Key, DependencyUsageRecord Record)> InKeyOrder =>
        Ordered(_counted).Select(kv => (kv.Key, kv.Value));

    /// <summary>
    /// How many uses were observed for one usage key, zero when it was never seen.
    /// </summary>
    public int CountFor(DependencyUsageKey key) => _counted.TryGetValue(key, out var record) ? record.Count : 0;

    /// <summary>
    /// Every use as a BW0017 row, counted ones first, so a baseline tool sees exactly what the
    /// build saw.
    /// </summary>
    public IEnumerable<Diagnostic> Observations() =>
        Observations(_counted, excepted: false).Concat(Observations(_excepted, excepted: true));

    /// <summary>
    /// The one ordering both outputs use. It sorts by all four parts of the key, because the
    /// source is a <see cref="ConcurrentDictionary{TKey, TValue}"/> whose order for anything a
    /// partial sort leaves tied varies from run to run.
    /// </summary>
    private static IOrderedEnumerable<KeyValuePair<DependencyUsageKey, DependencyUsageRecord>> Ordered(
        ConcurrentDictionary<DependencyUsageKey, DependencyUsageRecord> source) =>
        source
            .OrderBy(kv => kv.Key.Type, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.Kind)
            .ThenBy(kv => kv.Key.MemberId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.Site, StringComparer.Ordinal);

    private static IEnumerable<Diagnostic> Observations(
        ConcurrentDictionary<DependencyUsageKey, DependencyUsageRecord> source,
        bool excepted) =>
        Ordered(source)
            .Select(kv => ObservationDiagnosticFactory.Usage(
                kv.Key.Type, kv.Key.Kind, kv.Key.MemberId, kv.Key.Site, kv.Value.Count, kv.Value.File, excepted, kv.Value.IsTrackedOnly));
}
