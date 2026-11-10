using System.Collections.Immutable;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;

/// <summary>
/// The members a sealed restricted type declares, taken from the compilation that declares it.
/// </summary>
/// <param name="Type">Fully qualified metadata name of the sealed type.</param>
/// <param name="DeclaredMembers">Documentation-comment ids of every member the seal covers.</param>
internal sealed record DeclaredMemberSet(string Type, ImmutableArray<string> DeclaredMembers);
