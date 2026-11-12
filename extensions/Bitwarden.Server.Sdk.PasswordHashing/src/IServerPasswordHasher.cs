namespace Bitwarden.Server.Sdk.PasswordHashing;

/// <summary>
/// Hashes, on the server, the password hash a client sends as proof it knows a password, and checks
/// a later proof against the stored hash. Used for master password hashes and Send password hashes.
/// </summary>
/// <remarks>
/// The password is hashed with expensive work twice: once on the client, once on the server.
/// <code>
///  Client                                Server
///  ------                                ------
///  password
///     |
///  [slow KDF]  (1)
///     |
///  clientHash  -------- sent -------->  clientHash
///                                          |
///                                       [slow hash]  (2)
///                                          |
///                                       stored
/// </code>
/// (1) keeps the password on the client. The server only ever sees clientHash, and recovering the
/// password from it means guessing passwords through the slow KDF.
///
/// (2) keeps clientHash out of the database. clientHash is the credential: whoever sends it logs in
/// or opens the Send, no password needed. Stored as-is, anyone who reads the database (a leak, a
/// backup, an insider) could replay it. Stored after (2), the value is useless to send, because
/// <see cref="Verify"/> hashes whatever arrives, and recovering clientHash means paying for (1) and
/// (2) on every password guess, per salt.
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
