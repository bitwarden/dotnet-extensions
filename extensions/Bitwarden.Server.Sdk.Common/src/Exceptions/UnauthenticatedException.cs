namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// The caller presented no credential, or one that is not valid.
/// </summary>
/// <param name="detail">What to tell the caller.</param>
public sealed class UnauthenticatedException(string? detail = null)
    : Exception(detail ?? Errors.ErrorDetails.Unauthenticated);
