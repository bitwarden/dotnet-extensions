using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Turns an exception into the error a bulk response reports it as.
/// </summary>
/// <remarks>
/// The same mapping a single-resource request would get, because that is what the standards
/// require: an error's status must be what the single-resource API would have returned for that
/// resource. Shared with the exception handler rather than copied, so the two cannot disagree
/// about what a conflict or a missing resource looks like.
/// </remarks>
public static class BulkFailure
{
    /// <summary>
    /// Describes a resource that failed, by its position in the request.
    /// </summary>
    /// <param name="failure">Why it failed.</param>
    /// <param name="pointer">Where the caller named it: <c>/ids/2</c>, say.</param>
    public static Error At(Exception failure, string pointer)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var (status, code, title, detail, _) = ErrorResponseExceptionHandler.Describe(failure);

        return new Error
        {
            Status = status.ToString(),
            Code = code,
            Title = title,
            Detail = detail,
            Source = new ErrorSource { Pointer = pointer },
        };
    }
}
