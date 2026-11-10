using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// The resource addressed by the URL does not exist, or the caller may not know that it does.
/// </summary>
/// <remarks>
/// <para>
/// Answered with <c>404</c>, whether the resource is absent or the caller may not know about it, so
/// existence never leaks through the difference between <c>403</c> and <c>404</c>.
/// </para>
/// <para>
/// A resource referenced inside a payload rather than addressed by the URL is a <c>422</c> instead.
/// </para>
/// </remarks>
public sealed class NotFoundException : Exception
{
    /// <summary>
    /// The resource addressed by the URL does not exist.
    /// </summary>
    public NotFoundException() : base(ErrorDetails.NotFound)
    {
    }

    /// <summary>
    /// The named resource does not exist.
    /// </summary>
    /// <param name="resourceType">The resource type, singular, for example <c>user</c>.</param>
    /// <param name="id">The ID that was requested.</param>
    public NotFoundException(string resourceType, string id)
        : base(ErrorDetails.NotFoundById(resourceType, id))
    {
    }

    /// <summary>
    /// The resource does not exist, explained in the caller's terms.
    /// </summary>
    /// <param name="detail">What to tell the caller. Must not disclose anything they may not know.</param>
    public NotFoundException(string detail) : base(detail)
    {
    }
}
