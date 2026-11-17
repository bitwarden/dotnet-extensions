namespace Bitwarden.Server.Sdk.PasswordHashing;

/// <summary>
/// A hash of a <see cref="ClientCreatedHash"/>, safe for the server to store.
/// </summary>
/// <param name="Value">The hash as stored.</param>
public readonly record struct ServerStorableHash(string Value)
{
    /// <summary>
    /// Returns the type name only, so the hash never reaches a log.
    /// </summary>
    public override string ToString() => nameof(ServerStorableHash);
}
