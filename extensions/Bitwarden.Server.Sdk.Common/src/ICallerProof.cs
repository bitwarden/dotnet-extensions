namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// What proves whom the current request is for: the token a <see cref="CallerContextToken"/> is
/// exchanged from.
/// </summary>
/// <remarks>
/// At a product service, the token it validated. At a domain service, the context token it was
/// called with, once verified. None outside a request, for a request nobody signed in to, or for a
/// credential that names nobody.
/// </remarks>
public interface ICallerProof
{
    /// <summary>
    /// The encoded token, or null when the request has none.
    /// </summary>
    string? SubjectToken { get; }
}
