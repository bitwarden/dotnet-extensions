using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// The request conflicts with the resource's current state.
/// </summary>
/// <remarks>
/// Answered with <c>409</c>. The standards reserve this for a change racing another — most often a
/// version that no longer matches the stored one — rather than for a request that was invalid to
/// begin with, which is a <c>422</c>.
/// </remarks>
public sealed class ConflictException : Exception
{
    /// <summary>The resource changed since the caller last read it.</summary>
    public ConflictException() : base(ErrorDetails.Conflict)
    {
    }

    /// <summary>The request conflicts, explained in the caller's terms.</summary>
    /// <param name="detail">What to tell the caller.</param>
    public ConflictException(string detail) : base(detail)
    {
    }
}
