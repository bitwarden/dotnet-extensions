using System.Collections.Immutable;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Rules;
using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis.Usage;

/// <summary>
/// What one site key accumulated. Analyzer callbacks run concurrently, so every read and write of
/// the use list is taken under the same lock.
/// </summary>
internal sealed class DependencyUsageRecord
{
    private readonly List<(Location Location, ISymbol Subject)> _uses = [];

    public DependencyUsageRecord(RestrictedTypeModel model, ISymbol? member, string file)
    {
        Model = model;
        Member = member;
        File = file;
    }

    public RestrictedTypeModel Model { get; }
    public ISymbol? Member { get; }
    public string File { get; }

    /// <summary>
    /// Whether the rule governing this use allows new ones, which a baseline row has to carry so
    /// the shrink-only comparison can tell a row that is meant to rise from one that is not.
    /// </summary>
    public bool IsTrackedOnly => Model.RuleFor(Member).IsTrackedOnly;

    public int Count
    {
        get
        {
            lock (_uses)
            {
                return _uses.Count;
            }
        }
    }

    /// <summary>
    /// Every use with the symbol it reached the restricted type through. Uses with different
    /// subjects share a key, so a diagnostic for one use must name that use's own subject.
    /// </summary>
    public ImmutableArray<(Location Location, ISymbol Subject)> Uses
    {
        get
        {
            lock (_uses)
            {
                return [.. _uses];
            }
        }
    }

    public void Add(Location location, ISymbol subject)
    {
        lock (_uses)
        {
            _uses.Add((location, subject));
        }
    }
}
