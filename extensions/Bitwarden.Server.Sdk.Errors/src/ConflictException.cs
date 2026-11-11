namespace Bitwarden.Server.Sdk.Errors;

/// <summary>
/// The request conflicts with the resource's current state.
/// </summary>
/// <remarks>
/// A host answers it with <c>409</c>. For a change racing another, such as a version that no longer matches
/// the stored one. A request that was invalid to begin with is a <c>422</c> instead.
/// </remarks>
public sealed class ConflictException : Exception
{
    /// <summary>
    /// The resource changed since the caller last read it.
    /// </summary>
    public ConflictException() : base(ErrorDetails.Conflict)
    {
    }

    /// <summary>
    /// The request conflicts, explained in the caller's terms.
    /// </summary>
    /// <param name="detail">What to tell the caller.</param>
    public ConflictException(string detail) : base(detail)
    {
    }
}
