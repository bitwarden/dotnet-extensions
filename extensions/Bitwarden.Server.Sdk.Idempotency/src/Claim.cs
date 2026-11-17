namespace Bitwarden.Server.Sdk.Idempotency;

/// <summary>
/// What a claim found.
/// </summary>
/// <param name="Outcome">Whether the key is now held, was held already, or the store couldn't say.</param>
/// <param name="Existing">What was held already.</param>
internal readonly record struct Claim(ClaimOutcome Outcome, string? Existing = null)
{
    public static Claim Held => new(ClaimOutcome.Held);

    public static Claim Unavailable => new(ClaimOutcome.Unavailable);

    public static Claim Found(string existing) => new(ClaimOutcome.Found, existing);
}
