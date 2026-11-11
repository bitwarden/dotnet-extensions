using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.PasswordHashing;

internal sealed class ServerPasswordHasher : IServerPasswordHasher
{
    // Matches PasswordHasherKdfIterations on the Bitwarden server, so new hashes are identical in
    // shape to the ones it stores (V3: PBKDF2-HMAC-SHA512, 128-bit salt, 256-bit subkey).
    private const int Iterations = 100_000;

    // The hasher ignores the user it is given; one shared instance stands in for it.
    private static readonly object _user = new();

    // Own options, not the host's IOptions<PasswordHasherOptions>, so a host that tunes ASP.NET
    // Identity for its own users cannot change these hashes.
    private readonly PasswordHasher<object> _hasher = new(Options.Create(new PasswordHasherOptions
    {
        CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
        IterationCount = Iterations,
    }));

    public string Hash(string clientHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientHash);

        return _hasher.HashPassword(_user, clientHash);
    }

    public PasswordHashVerification Verify(string? storedHash, string? clientHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash) || string.IsNullOrWhiteSpace(clientHash))
        {
            return PasswordHashVerification.Failed;
        }

        // Identity throws on a stored hash that is not base64; treat it as no match.
        PasswordVerificationResult result;
        try
        {
            result = _hasher.VerifyHashedPassword(_user, storedHash, clientHash);
        }
        catch (FormatException)
        {
            return PasswordHashVerification.Failed;
        }

        // Older hashes (V2, or V3 with fewer iterations) still verify; the result asks for a rehash.
        return result switch
        {
            PasswordVerificationResult.Success => PasswordHashVerification.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordHashVerification.SuccessRehashNeeded,
            _ => PasswordHashVerification.Failed,
        };
    }
}
