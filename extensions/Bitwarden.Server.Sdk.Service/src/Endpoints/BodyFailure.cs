namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// A plain body the platform could not read.
/// </summary>
/// <param name="StatusCode">422 for a body that is not the JSON the operation takes, 415 for one that is not JSON.</param>
/// <param name="Key">Where the failure is, as an MVC controller's model state would key it.</param>
/// <param name="Message">What is wrong, in an MVC controller's words.</param>
internal sealed record BodyFailure(int StatusCode, string Key, string Message);
