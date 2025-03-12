namespace Bitwarden.OPAQUE;

///  A VOPRF ciphersuite
public enum OprfCS
{
    /// The Ristretto255 ciphersuite
    Ristretto255 = 0,
}

/// A `Group` used for the `KeyExchange`.
public enum KeGroup
{
    /// The Ristretto255 group
    Ristretto255
}

/// The key exchange protocol to use in the login step
public enum KeyExchange
{
    /// The Triple Diffie-Hellman key exchange implementation
    TripleDH
}

/// A key stretching function, typically used for password hashing
public abstract record KSF;

/// <summary>
/// Argon2id key stretching function
/// </summary>
/// <param name="iterations">Iteration count</param>
/// <param name="memoryKiB">Memory in KibiBytes</param>
/// <param name="parallelism">Parallelism count</param>
public record Argon2id(int iterations, int memoryKiB, int parallelism) : KSF;

/// Configures the underlying primitives used in OPAQUE
public struct CipherConfiguration
{
    ///  A VOPRF ciphersuite
    public OprfCS OprfCS;
    /// A `Group` used for the `KeyExchange`.
    public KeGroup KeGroup;
    /// The key exchange protocol to use in the login step
    public KeyExchange KeyExchange;
    /// A key stretching function, typically used for password hashing
    public KSF KSF;

    /// The default configuration for the OPAQUE protocol
    public static readonly CipherConfiguration Default = new CipherConfiguration
    {
        OprfCS = OprfCS.Ristretto255,
        KeGroup = KeGroup.Ristretto255,
        KeyExchange = KeyExchange.TripleDH,
        KSF = new Argon2id(4, 65536, 4)
    };
}
