namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// What a bulk operation does when some of the resources fail.
/// </summary>
/// <remarks>
/// Chosen by the caller with the <c>atomic</c> query parameter, and constrained by what the
/// operation supports. An operation must recognise the parameter even where it supports only
/// one mode: ignoring it would change the outcome rather than merely do less, which is the one
/// thing an ignored parameter must never do.
/// </remarks>
[Flags]
public enum Atomicity
{
    /// <summary>
    /// No resource changes unless every resource succeeds: <c>atomic=true</c>, and the default.
    /// </summary>
    /// <remarks>
    /// The default because it is the answer a caller who has not thought about it wants. Half a
    /// change nobody asked for is worse than no change, and the caller can always ask for the
    /// other.
    /// </remarks>
    AllOrNothing = 1,

    /// <summary>
    /// Each resource succeeds or fails on its own: <c>atomic=false</c>.
    /// </summary>
    /// <remarks>
    /// One resource's failure neither prevents nor undoes another's success. For an import, or
    /// anywhere the caller can report and retry the failures individually.
    /// </remarks>
    BestEffort = 2,
}
