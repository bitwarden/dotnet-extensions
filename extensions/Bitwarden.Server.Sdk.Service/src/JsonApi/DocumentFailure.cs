using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// The errors a request body failed with, and the status that describes them.
/// </summary>
/// <param name="StatusCode">400 when the body could not be parsed, 422 when it could.</param>
/// <param name="Errors">Every error found at the level that failed.</param>
internal sealed record DocumentFailure(int StatusCode, IReadOnlyList<Error> Errors);
