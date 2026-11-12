namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// What an unlock method's <c>data</c> was.
/// </summary>
public enum UnlockMethodKind
{
    /// <summary><c>master-password</c>.</summary>
    MasterPassword,

    /// <summary><c>key-connector</c>.</summary>
    KeyConnector,

    /// <summary><c>device</c>.</summary>
    Device,

    /// <summary><c>passkey</c>.</summary>
    Passkey,
}
