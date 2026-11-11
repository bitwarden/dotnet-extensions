namespace Bitwarden.Server.Sdk.PasswordHashing;

/// <summary>
/// Hashes a password hash a client derived, so the server stores no value a client can replay.
/// Used for master password hashes and Send password hashes alike.
/// </summary>
public interface IServerPasswordHasher
{
    /// <summary>
    /// Returns a salted hash of <paramref name="clientHash"/>, to store.
    /// </summary>
    /// <param name="clientHash">The password hash sent by the client.</param>
    /// <returns>The hash to store.</returns>
    /// <exception cref="ArgumentException"><paramref name="clientHash"/> is null, empty or white space.</exception>
    string Hash(string clientHash);

    /// <summary>
    /// Checks <paramref name="clientHash"/> against <paramref name="storedHash"/>.
    /// </summary>
    /// <param name="storedHash">A hash returned by <see cref="Hash"/>, or stored by the Bitwarden server.</param>
    /// <param name="clientHash">The password hash sent by the client.</param>
    /// <returns>
    /// <see cref="PasswordHashVerification.Failed"/> when either value is null, empty or white space,
    /// <paramref name="storedHash"/> is malformed, or the values do not match.
    /// </returns>
    PasswordHashVerification Verify(string? storedHash, string? clientHash);
}
