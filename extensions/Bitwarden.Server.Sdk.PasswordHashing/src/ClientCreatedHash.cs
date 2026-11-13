namespace Bitwarden.Server.Sdk.PasswordHashing;

/// <summary>
/// A password hash a client derived and sent as proof it knows a password.
/// </summary>
/// <param name="Value">The hash as sent by the client.</param>
public readonly record struct ClientCreatedHash(string Value)
{
    /// <summary>
    /// Returns the type name only, so the credential never reaches a log.
    /// </summary>
    public override string ToString() => nameof(ClientCreatedHash);
}
