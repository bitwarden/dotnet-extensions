using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// What the token endpoint answers with.
/// </summary>
/// <param name="AccessToken">The token to present.</param>
/// <param name="ExpiresIn">Its lifetime, in seconds.</param>
internal sealed record IssuedToken(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);
