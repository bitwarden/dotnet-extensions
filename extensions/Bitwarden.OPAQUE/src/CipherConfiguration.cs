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

/// A key stretching algorithm
public enum KsfAlgorithm
{
    /// The Argon2id key stretching function
    Argon2id
}

/// Key stretching function parameters
public struct KsfParameters
{
    /// The number of iterations to use
    public int Iterations;
    /// The amount of memory to use in KiB
    public int Memory;
    /// The number of threads to use
    public int Parallelism;
}

/// A key stretching function, typically used for password hashing
public struct Ksf
{
    /// The key stretching function to use
    public KsfAlgorithm Algorithm;
    /// The parameters for the key stretching function
    public KsfParameters Parameters;
}

/// Configures the underlying primitives used in OPAQUE
public struct CipherConfiguration
{
    /// The version of the OPAQUE-ke protocol to use
    public int OpaqueVersion;
    ///  A VOPRF ciphersuite
    public OprfCS OprfCS;
    /// A `Group` used for the `KeyExchange`.
    public KeGroup KeGroup;
    /// The key exchange protocol to use in the login step
    public KeyExchange KeyExchange;
    /// A key stretching function, typically used for password hashing
    public Ksf Ksf;

    /// The default configuration for the OPAQUE protocol
    public static readonly CipherConfiguration Default = new CipherConfiguration
    {
        OpaqueVersion = 3,
        OprfCS = OprfCS.Ristretto255,
        KeGroup = KeGroup.Ristretto255,
        KeyExchange = KeyExchange.TripleDH,
        Ksf = new Ksf
        {
            Algorithm = KsfAlgorithm.Argon2id,
            Parameters = new KsfParameters
            {
                Iterations = 4,
                Memory = 65536,
                Parallelism = 4
            }
        }
    };
}
