namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// One failure, keyed as an MVC controller's model state keys it: <c>Data.Email</c>, <c>Items[1].Name</c>.
/// </summary>
/// <param name="Key">Where.</param>
/// <param name="Message">What.</param>
internal readonly record struct ModelError(string Key, string Message);
