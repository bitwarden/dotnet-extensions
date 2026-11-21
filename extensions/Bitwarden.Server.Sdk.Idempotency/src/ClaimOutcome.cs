namespace Bitwarden.Server.Sdk.Idempotency;

/// <summary>
/// What a claim found.
/// </summary>
internal enum ClaimOutcome
{
    /// <summary>
    /// Nothing was held: the write is now this request's to run.
    /// </summary>
    Held,

    /// <summary>
    /// Something was held: a write running, or an answer.
    /// </summary>
    Found,

    /// <summary>
    /// The store couldn't say.
    /// </summary>
    Unavailable,
}
