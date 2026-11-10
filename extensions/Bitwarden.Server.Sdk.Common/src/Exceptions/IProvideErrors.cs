namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// An exception that already knows what the response should say.
/// </summary>
/// <remarks>
/// For a rule that can fail in several places at once, such as validating a list, so that every
/// problem is reported rather than only the first.
/// </remarks>
public interface IProvideErrors
{
    /// <summary>
    /// The status the response carries.
    /// </summary>
    int StatusCode => 422;

    /// <summary>
    /// The errors to report, in the order the caller should read them.
    /// </summary>
    IReadOnlyList<Errors.Error> Errors { get; }
}
