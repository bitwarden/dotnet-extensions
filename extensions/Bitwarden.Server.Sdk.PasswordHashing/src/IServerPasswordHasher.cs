namespace Bitwarden.Server.Sdk.PasswordHashing;

/// <summary>
/// Hashes, on the server, the password hash a client sends as proof it knows a password, and checks
/// a later proof against the stored hash. Used for master password hashes and Send password hashes.
/// </summary>
/// <remarks>
/// <code>
///  Client                                          Server
///  ------                                          ------
///  Master password:
///    masterKey  = KDF(password, email)
///    clientHash = PBKDF2-SHA256(masterKey,
///                   salt: password, 1 iteration)
///  Send password:
///    clientHash = PBKDF2-SHA256(password,
///                   salt: Send key, 100,000 iterations)
///
///  Create or change password:
///    clientHash  -------------------------------&gt;  stored = Hash(clientHash)
///                                                   (random salt, PBKDF2-SHA512,
///                                                    100,000 iterations)
///  Log in or open Send:
///    clientHash  -------------------------------&gt;  Verify(stored, clientHash)
/// </code>
/// The client hash is the credential itself: the server accepts whoever sends it, without needing
/// the password. Stored as-is, anyone who reads the database (a leak, a backup, an insider) could
/// send it and log in or open the Send. Stored as <see cref="Hash"/> of it, the stored value is
/// useless as a credential, because <see cref="Verify"/> hashes what is sent and the stored value
/// does not hash to itself. Recovering the client hash from it means guessing the password, at the
/// cost of the client derivation plus 100,000 PBKDF2-SHA512 iterations per guess, and per salt.
/// </remarks>
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
