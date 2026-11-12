using Bitwarden.Server.Sdk.Errors;

namespace Bitwarden.Server.Sdk.Resources.Querying;

/// <summary>
/// Turns an exception into the error a bulk response reports it as.
/// </summary>
/// <remarks>
/// The same mapping a single-resource request would get, because that is what the standards
/// require: an error's status must be what the single-resource API would have returned for that
/// resource. It follows the exception handler's order, sharing its steps rather than copying them,
/// so the two cannot disagree about what a conflict or a missing resource looks like: a registered
/// translator's answer first, then an exception that provides its own errors, then the platform's
/// mapping.
/// </remarks>
public static class BulkFailure
{
    /// <summary>
    /// Describes a resource that failed, by its position in the request.
    /// </summary>
    /// <param name="failure">Why it failed.</param>
    /// <param name="pointer">Where the caller named it: <c>/ids/2</c>, say.</param>
    public static Error At(Exception failure, string pointer) => At(failure, pointer, []);

    /// <summary>
    /// Describes a resource that failed, by its position in the request, translating a library's
    /// exception as the exception handler would.
    /// </summary>
    /// <remarks>
    /// An exception that provides its own errors is reported by the first of them, with the status
    /// the exception carries and the pointer given here.
    /// </remarks>
    /// <param name="failure">Why it failed.</param>
    /// <param name="pointer">Where the caller named it: <c>/ids/2</c>, say.</param>
    /// <param name="translators">Say which platform exception a library's exception means.</param>
    public static Error At(Exception failure, string pointer, IEnumerable<IExceptionTranslator> translators)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(translators);

        var answered = ErrorResponseExceptionHandler.Translate(failure, translators);

        if (answered is IProvideErrors provided && provided.Errors.Count > 0)
        {
            return provided.Errors[0] with
            {
                Status = provided.StatusCode.ToString(),
                Source = new ErrorSource { Pointer = pointer },
            };
        }

        var (status, code, title, detail, _) = ErrorResponseExceptionHandler.Describe(answered);

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
