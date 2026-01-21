using System.Net.Http.Json;
using Bitwarden.Core.Json;
using Bitwarden.Core.Models;

namespace Bitwarden.Core;

public sealed class BitwardenSecretsClient
{
    public static BitwardenSecretsClient Create(Uri apiUri, BitwardenIdentityClient identityClient, AccessToken accessToken, HttpMessageHandler httpMessageHandler)
    {
        var authenticatingHandler = new IdentityAuthenticatingHandler(identityClient, new IdentityAuthenticatingOptions
        {
            Authenticate = async c => await c.AccessTokenLoginAsync(accessToken.ClientId, accessToken.ClientSecret),
        })
        {
            InnerHandler = httpMessageHandler
        };
        return new BitwardenSecretsClient(new HttpClient(authenticatingHandler)
        {
            BaseAddress = apiUri,
        });
    }

    private readonly HttpClient _apiClient;

    public BitwardenSecretsClient(HttpClient apiClient)
    {
        // TODO: Create inner handler for exception handling
        _apiClient = apiClient;
    }

    public async Task<ProjectResponseModel> GetProjectInfoAsync(Guid projectId)
    {
        return (await _apiClient.GetFromJsonAsync($"/projects/{projectId}",
            BitwardenSerializerContext.Default.ProjectResponseModel))!;
    }

    public async Task<SecretWithProjectsListResponseModel> GetSecretsAsync(Guid projectId)
    {
        return (await _apiClient.GetFromJsonAsync($"/projects/{projectId}/secrets",
            BitwardenSerializerContext.Default.SecretWithProjectsListResponseModel))!;
    }

    public async Task<SecretResponseModel> UpdateSecretAsync(Guid secretId, SecretUpdateRequestModel request)
    {
        var response = await _apiClient.PutAsJsonAsync($"/secrets/{secretId}", request,
            BitwardenSerializerContext.Default.SecretUpdateRequestModel);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync(BitwardenSerializerContext.Default.SecretResponseModel))!;
    }

    public async Task<SecretResponseModel> GetSecretAsync(Guid secretId)
    {
        return (await _apiClient.GetFromJsonAsync($"/secrets/{secretId}",
            BitwardenSerializerContext.Default.SecretResponseModel))!;
    }
}
