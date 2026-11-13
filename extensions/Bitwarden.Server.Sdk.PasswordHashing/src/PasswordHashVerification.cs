namespace Bitwarden.Server.Sdk.PasswordHashing;

/// <summary>
/// The outcome of checking a client password hash against a stored hash.
/// </summary>
public enum PasswordHashVerification
{
    /// <summary>
    /// The client password hash does not match.
    /// </summary>
    Failed,

    /// <summary>
    /// The client password hash matches.
    /// </summary>
    Success,

    /// <summary>
    /// The client password hash matches, but the stored hash uses older parameters. Store
    /// <see cref="IServerPasswordHasher.CreateStoredHash"/> of the client password hash in its place.
    /// </summary>
    SuccessRehashNeeded,
}
