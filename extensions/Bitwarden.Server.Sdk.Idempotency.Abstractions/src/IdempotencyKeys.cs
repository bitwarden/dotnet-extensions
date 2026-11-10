namespace Bitwarden.Server.Sdk.Idempotency;

/// <summary>
/// The <c>Idempotency-Key</c> request header (IETF draft "The Idempotency-Key HTTP Header Field"), which a client
/// sends on a write so that the service receiving it can answer a repeat from what it recorded the first time.
/// </summary>
/// <remarks>
/// <para>
/// A write that failed in transit (the connection dropped, a proxy failed) may or may not have been done. A service
/// that honours the header does a write sent again with the same key at most once, replaying the first completed
/// answer rather than doing it twice. How it answers a repeat with a different body, or one that arrives while the
/// first is still running, and which callers' keys it honours, are the service's to decide.
/// </para>
/// </remarks>
public static class IdempotencyKeys
{
    /// <summary>
    /// The header.
    /// </summary>
    public const string Header = "Idempotency-Key";

    /// <summary>
    /// The header a service puts on a replayed answer, <c>true</c>, so a log or a test can tell a replay from a first
    /// answer.
    /// </summary>
    public const string ReplayedHeader = "Idempotency-Replayed";

    /// <summary>
    /// The longest key accepted.
    /// </summary>
    public const int MaximumLength = 255;

    /// <summary>
    /// Whether <paramref name="key"/> is a key a service accepts: 1 to <see cref="MaximumLength"/> visible ASCII
    /// characters.
    /// </summary>
    /// <param name="key">The header's value.</param>
    public static bool IsValid(string? key) =>
        key is { Length: > 0 and <= MaximumLength } && key.All(character => character is > ' ' and <= '~');
}
