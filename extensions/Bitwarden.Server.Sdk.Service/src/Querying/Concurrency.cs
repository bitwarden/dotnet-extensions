using Bitwarden.Server.Sdk.Common;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Refuses a write made against a version of a resource that has since moved on.
/// </summary>
/// <remarks>
/// The standards require a <c>409</c> when a caller's <c>version</c> does not match the stored one,
/// and a caller who sends none is saying they accept whatever is there. Both halves matter: making
/// the version mandatory would break every caller that does not care, and ignoring it would
/// silently lose the change of whoever wrote first.
/// </remarks>
public static class Concurrency
{
    /// <summary>
    /// Refuses the write unless the caller's version matches the stored one.
    /// </summary>
    /// <param name="sent">What the caller said the resource was at, or null if they did not say.</param>
    /// <param name="stored">What it is actually at.</param>
    /// <exception cref="ConflictException">It has changed since the caller read it.</exception>
    public static void Require(string? sent, int stored)
    {
        if (sent is not null && sent != stored.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            throw new ConflictException();
        }
    }
}
