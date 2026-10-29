namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// The caller is known but may not perform this operation.
/// </summary>
/// <param name="detail">What to tell the caller.</param>
public sealed class ForbiddenException(string? detail = null)
    : Exception(detail ?? Errors.ErrorDetails.Forbidden);
