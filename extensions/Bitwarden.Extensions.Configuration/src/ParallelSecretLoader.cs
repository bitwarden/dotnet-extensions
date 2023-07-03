using Bitwarden.Core;
using Bitwarden.Core.Models;

namespace Bitwarden.Extensions.Configuration;

// Based on: https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/extensions/Azure.Extensions.AspNetCore.Configuration.Secrets/src/ParallelSecretLoader.cs
// (https://github.com/Azure/azure-sdk-for-net/blob/0f79c296c1e5f119337db1378ce834da3064f368/sdk/extensions/Azure.Extensions.AspNetCore.Configuration.Secrets/src/ParallelSecretLoader.cs)
internal class ParallelSecretLoader : IDisposable
{
    private const int ParallelismLevel = 32;
    private readonly BitwardenSecretsClient _client;
    private readonly SemaphoreSlim _semaphore;
    private readonly List<Task<SecretResponseModel>> _tasks;

    public ParallelSecretLoader(BitwardenSecretsClient client)
    {
        _client = client;
        _semaphore = new SemaphoreSlim(ParallelismLevel, ParallelismLevel);
        _tasks = new();
    }

    public void Add(Guid secretId)
    {
        _tasks.Add(GetSecret(secretId));
    }

    private async Task<SecretResponseModel> GetSecret(Guid secretId)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            return await _client.GetSecretAsync(secretId).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public Task<SecretResponseModel[]> WaitForAll()
    {
        return Task.WhenAll(_tasks);
    }

    public void Dispose()
    {
        _semaphore?.Dispose();
    }
}
