using Bitwarden.Core;
using Bitwarden.Core.Json;
using Bitwarden.Core.Models;

using Microsoft.Extensions.Configuration;

namespace Bitwarden.Extensions.Configuration;

public class SecretsManagerConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly TimeSpan? _reloadInterval;
    private readonly CancellationTokenSource _cancellationToken;
    private readonly Guid _projectId;
    private readonly AccessToken _accessToken;
    private readonly BitwardenEnvironment _environment;

    private readonly HttpMessageHandler _innerHandler;
    private readonly BitwardenIdentityClient _identityClient;

    private Dictionary<Guid, SecretResponseModel>? _loadedSecrets;
    private Task? _pollingTask;
    private SymmetricCryptoKey? _organizationEncryptionKey;
    private bool _disposed;

    public SecretsManagerConfigurationProvider(SecretsManagerConfigurationOptions options)
    {
        if (options.ReloadInterval.HasValue && options.ReloadInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.ReloadInterval), options.ReloadInterval, $"{nameof(options.ReloadInterval)} must be positive.");
        }

        if (options.AccessToken is null)
        {
            throw new ArgumentNullException(nameof(options.AccessToken), "AccessToken must not be null.");
        }

        _projectId = options.ProjectId;
        _reloadInterval = options.ReloadInterval;
        _accessToken = options.AccessToken;
        _environment = options.Environment;

        _cancellationToken = new CancellationTokenSource();
        _innerHandler = new SocketsHttpHandler();
        _identityClient = BitwardenIdentityClient.Create(options.Environment.IdentityUri, _innerHandler);
    }
    public override void Load() => LoadAsync().GetAwaiter().GetResult();

    private async Task PollForSecretChangesAsync()
    {
        while (!_cancellationToken.IsCancellationRequested)
        {
            await WaitForReload().ConfigureAwait(false);
            try
            {
                await LoadAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore
            }
        }
    }

    private Task WaitForReload()
    {
        return Task.Delay(_reloadInterval!.Value, _cancellationToken.Token);
    }
    // internal for testing.
    internal async Task LoadAsync()
    {
        if (_organizationEncryptionKey is null)
        {
            var accessTokenAuthenticationPayload = await _identityClient.AccessTokenLoginAsync(
            _accessToken.ClientId, _accessToken.ClientSecret);

            var decryptedPayload = accessTokenAuthenticationPayload.EncryptedPayload
                .Decrypt(_accessToken.EncryptionKey, BitwardenSerializerContext.Default.Payload)!;

            _organizationEncryptionKey = decryptedPayload.EncryptionKey;
        }

        var secretsClient = BitwardenSecretsClient.Create(_environment.ApiUri,
            _identityClient, _accessToken, _innerHandler);

        using var secretLoader = new ParallelSecretLoader(secretsClient);
        var newLoadedSecrets = new Dictionary<Guid, SecretResponseModel>();
        var oldLoadedSecrets = Interlocked.Exchange(ref _loadedSecrets, null);

        var secrets = await secretsClient.GetSecretsAsync(_projectId);

        await foreach (var slimSecret in secrets.Secrets)
        {
            var secretId = slimSecret.Id;
            if (oldLoadedSecrets != null
                && oldLoadedSecrets.TryGetValue(secretId, out var existingSecret)
                && IsUpToDate(existingSecret, slimSecret))
            {
                oldLoadedSecrets.Remove(secretId);
                newLoadedSecrets.Add(secretId, existingSecret);
            }
            else
            {
                secretLoader.Add(secretId);
            }
        }

        var loadedSecrets = await secretLoader.WaitForAll().ConfigureAwait(false);
        foreach (var loadedSecret in loadedSecrets)
        {
            newLoadedSecrets.Add(loadedSecret.Id, loadedSecret);
        }

        _loadedSecrets = newLoadedSecrets;

        if (loadedSecrets.Any() || oldLoadedSecrets?.Any() is true)
        {
            // Technically two secrets could have the same key, this will cause an issue,
            // we should implement something that takes the most recently updated one
            Data = newLoadedSecrets.Values.ToDictionary<SecretResponseModel, string, string?>(
                s => s.Key.DecryptToString(_organizationEncryptionKey),
                s => s.Value.DecryptToString(_organizationEncryptionKey),
                StringComparer.OrdinalIgnoreCase);
            if (oldLoadedSecrets != null)
            {
                OnReload();
            }
        }

        if (_pollingTask == null && _reloadInterval != null)
        {
            _pollingTask = PollForSecretChangesAsync();
        }
    }

    private static bool IsUpToDate(SecretResponseModel current, SecretWithProjectsListResponseModel.Secret secretProperties)
    {
        return current.RevisionDate == secretProperties.RevisionDate;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (!_disposed)
            {
                _innerHandler.Dispose();
                _cancellationToken.Cancel();
                _cancellationToken.Dispose();
            }

            _disposed = true;
        }
    }

    public override string ToString()
    {
        // Don't bother showing a pretty name for any other environment.
        var environmentName = _environment == BitwardenEnvironment.CloudUS ? "Cloud" : "Custom";
        return $"{GetType().Name} ProjectId: {_projectId} Environment: '{environmentName}'";
    }
}
