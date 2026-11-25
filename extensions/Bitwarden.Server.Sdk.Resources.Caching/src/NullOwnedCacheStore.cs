namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// No cache: the default outside cloud.
/// </summary>
internal sealed class NullOwnedCacheStore : IOwnedCacheStore
{
    public bool Enabled => false;

    public Task<IReadOnlyDictionary<string, string>?> ReadAsync(string key) =>
        Task.FromResult<IReadOnlyDictionary<string, string>?>(null);

    public Task<bool> FillAsync(string key, long version, string? owner, string? data, TimeSpan lifetime) =>
        Task.FromResult(false);

    public Task InvalidateAsync(IReadOnlyCollection<Invalidation> invalidations, TimeSpan lifetime, TimeSpan arrivalLifetime) =>
        Task.CompletedTask;
}
