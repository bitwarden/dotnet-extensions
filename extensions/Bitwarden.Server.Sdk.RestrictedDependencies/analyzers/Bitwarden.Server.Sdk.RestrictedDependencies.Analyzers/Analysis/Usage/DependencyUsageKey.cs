
namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis.Usage;

/// <summary>
/// Identifies one aggregated use: the restricted type, the usage kind, the restricted member for
/// member uses, and the documentation-comment id of the member that contains the use. Uses of the
/// same kind in the same member collapse onto one key and are counted.
/// </summary>
/// <param name="Type">Fully qualified metadata name of the restricted type.</param>
/// <param name="Kind">The usage kind.</param>
/// <param name="MemberId">Documentation-comment id of the restricted member; null except for member uses.</param>
/// <param name="Site">Documentation-comment id of the containing member.</param>
internal readonly record struct DependencyUsageKey(string Type, DependencyUsageType Kind, string? MemberId, string Site);
