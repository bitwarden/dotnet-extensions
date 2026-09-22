using System.Collections.Immutable;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Rules;
using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis.Usage;

/// <summary>
/// What one site key accumulated. Analyzer callbacks run concurrently, so every read and write of
/// the location list is taken under the same lock.
/// </summary>
internal sealed class DependencyUsageRecord
{
    private readonly List<Location> _locations = [];

    public DependencyUsageRecord(RestrictedTypeModel model, ISymbol? member, ISymbol subject, string file)
    {
        Model = model;
        Member = member;
        Subject = subject;
        File = file;
    }

    public RestrictedTypeModel Model { get; }
    public ISymbol? Member { get; }
    public ISymbol Subject { get; }
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
            lock (_locations)
            {
                return _locations.Count;
            }
        }
    }

    public ImmutableArray<Location> Locations
    {
        get
        {
            lock (_locations)
            {
                return [.. _locations];
            }
        }
    }

    public void Add(Location location)
    {
        lock (_locations)
        {
            _locations.Add(location);
        }
    }
}
