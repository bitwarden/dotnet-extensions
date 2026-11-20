namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// A foreign key a cached read declares a database cascade through, and why that leaves no entry stale.
/// </summary>
/// <param name="Dependent">The row the key belongs to.</param>
/// <param name="Properties">The key's properties, in order.</param>
/// <param name="Coverage">Why no entry is left stale.</param>
/// <param name="Reason">The declaration's reason, for its reader.</param>
internal sealed record CascadeDeclaration(Type Dependent, IReadOnlyList<string> Properties, CascadeCoverage Coverage, string Reason);
