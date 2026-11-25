namespace Bitwarden.Server.Sdk.Idempotency;

/// <summary>
/// Where a service keeps, by key, the writes it is running and the answers it gave: sealed values the store never
/// reads.
/// </summary>
internal interface IIdempotencyStore
{
    /// <summary>
    /// Holds <paramref name="key"/> for a write about to run, unless something is already held under it.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <param name="pending">What to hold while the write runs.</param>
    /// <param name="lease">How long to hold it if the write never finishes.</param>
    /// <returns>
    /// <see cref="Claim.Held"/> when it is now held; what was held already otherwise; or
    /// <see cref="Claim.Unavailable"/> when the store can't say, and the write runs without a key.
    /// </returns>
    ValueTask<Claim> ClaimAsync(string key, string pending, TimeSpan lease);

    /// <summary>
    /// Replaces what <paramref name="key"/> holds with the write's answer.
    /// </summary>
    ValueTask CompleteAsync(string key, string answer, TimeSpan retention);

    /// <summary>
    /// Lets go of <paramref name="key"/>, if it still holds <paramref name="pending"/>: the write failed.
    /// </summary>
    ValueTask ReleaseAsync(string key, string pending);
}
