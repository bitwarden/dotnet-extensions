using System.Net.Http.Json;
using System.Text.Json;

namespace Bitwarden.Core;

public sealed class BitwardenIdentityClient
{
    public static BitwardenIdentityClient Create(Uri identityUri, HttpMessageHandler httpMessageHandler)
    {
        return new BitwardenIdentityClient(new HttpClient(httpMessageHandler)
        {
            BaseAddress = identityUri,
        });
    }

    private readonly HttpClient _identityClient;

    public BitwardenIdentityClient(HttpClient identityClient)
    {
        _identityClient = identityClient;
    }

    public async Task<JsonDocument> ConnectTokenAsync(Dictionary<string, string> formContent)
    {
        var response = await _identityClient.PostAsync("connect/token", new FormUrlEncodedContent(formContent));

        response.EnsureSuccessStatusCode();

        // TODO: Package errors into helpful exceptions
        return (await response.Content.ReadFromJsonAsync<JsonDocument>())!;
    }

    // TODO: Use better types
    public async Task<AccessTokenPayload> AccessTokenLoginAsync(Guid clientId, string clientSecret)
    {
        using var responseBody = await ConnectTokenAsync(new Dictionary<string, string>
        {
            { "scope", "api.secrets" },
            { "client_id", clientId.ToString() },
            { "client_secret", clientSecret },
            { "grant_type", "client_credentials" }
        });

        var authenticationPayload = ReadPayloadDetails(responseBody);

        if (!responseBody.RootElement.TryGetProperty("encrypted_payload", out var encryptedPayloadElement) || encryptedPayloadElement.ValueKind != JsonValueKind.String)
        {
            throw CreateMissingPropertyException("encrypted_payload");
        }

        return new AccessTokenPayload(
            authenticationPayload.AccessToken,
            authenticationPayload.ExpiresIn,
            authenticationPayload.TokenType,
            EncryptedString.Parse(encryptedPayloadElement.GetString()));
    }

    public async Task<RefreshableAuthenticationPayload> RefreshLoginAsync(string refreshToken)
    {
        using var responseBody = await ConnectTokenAsync(new Dictionary<string, string>
        {
            { "grant_type", "refresh_token" },
            { "client_id", "sdk" },
            { "refresh_token", refreshToken },
        });

        var (accessToken, expiresIn, tokenType) = ReadPayloadDetails(responseBody);
        var newRefreshToken = ReadRefreshToken(responseBody);
        return new RefreshableAuthenticationPayload(accessToken, expiresIn, tokenType, newRefreshToken);
    }

    internal static (JwtToken AccessToken, TimeSpan ExpiresIn, string TokenType) ReadPayloadDetails(JsonDocument jsonDocument)
    {
        var root = jsonDocument.RootElement;

        if (!root.TryGetProperty("access_token", out var accessTokenElement) || accessTokenElement.ValueKind != JsonValueKind.String)
        {
            throw CreateMissingPropertyException("access_token");
        }

        if (!root.TryGetProperty("expires_in", out var expiresInElement) || expiresInElement.ValueKind != JsonValueKind.Number)
        {
            throw CreateMissingPropertyException("expires_in");
        }

        if (!root.TryGetProperty("token_type", out var tokenTypeElement) || tokenTypeElement.ValueKind != JsonValueKind.String)
        {
            throw CreateMissingPropertyException("token_type");
        }

        return (JwtToken.Parse(accessTokenElement.GetString()!, null),
            TimeSpan.FromSeconds(expiresInElement.GetInt32()),
            tokenTypeElement.GetString()!);
    }

    private static string ReadRefreshToken(JsonDocument jsonDocument)
    {
        if (!jsonDocument.RootElement.TryGetProperty("refresh_token", out var refreshTokenElement) || refreshTokenElement.ValueKind != JsonValueKind.String)
        {
            throw CreateMissingPropertyException("refresh_token");
        }

        return refreshTokenElement.GetString()!;
    }

    private static HttpRequestException CreateMissingPropertyException(string propertyName)
    {
        return new HttpRequestException($"Response indicated success, but {propertyName} was missing from the body.");
    }
}
