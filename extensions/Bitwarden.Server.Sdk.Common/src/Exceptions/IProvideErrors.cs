namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// An exception that already knows what the response should say.
/// </summary>
/// <remarks>
/// The platform maps the exceptions it defines to error documents itself. A service with a rule
/// of its own — particularly one that can fail in several places at once, such as validating a
/// list — needs to report more than the one error those carry, and without this would have to
/// choose between reporting the first problem or inventing its own response shape.
/// </remarks>
public interface IProvideErrors
{
    /// <summary>The status the response carries.</summary>
    int StatusCode => 422;

    /// <summary>The errors to report, in the order the caller should read them.</summary>
    IReadOnlyList<JsonApi.Error> Errors { get; }
}
