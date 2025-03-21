using System.Text.Json.Serialization;

namespace Bitwarden.Opaque;

///  A VOPRF ciphersuite
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OprfCs
{
    /// The Ristretto255 ciphersuite
    Ristretto255 = 0,
}

/// A `Group` used for the `KeyExchange`.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum KeGroup
{
    /// The Ristretto255 group
    Ristretto255
}

/// The key exchange protocol to use in the login step
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum KeyExchange
{
    /// The Triple Diffie-Hellman key exchange implementation
    TripleDH
}

/// A key stretching algorithm
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum KsfAlgorithm
{
    /// The Argon2id key stretching function
    Argon2id
}

/// Key stretching function parameters
public class KsfParameters
{
    /// The number of iterations to use
    public int Iterations { get; set; }
    /// The amount of memory to use in KiB
    public int Memory { get; set; }
    /// The number of threads to use
    public int Parallelism { get; set; }
}

/// A key stretching function, typically used for password hashing
public class Ksf
{
    /// The key stretching function to use
    public KsfAlgorithm Algorithm { get; set; }
    /// The parameters for the key stretching function
    public required KsfParameters Parameters { get; set; }
}

/// Configures the underlying primitives used in OPAQUE
public class CipherConfiguration
{
    /// The version of the OPAQUE-ke protocol to use
    public int OpaqueVersion { get; set; }
    ///  A VOPRF ciphersuite
    public OprfCs OprfCs { get; set; }
    /// A `Group` used for the `KeyExchange`.
    public KeGroup KeGroup { get; set; }
    /// The key exchange protocol to use in the login step
    public KeyExchange KeyExchange { get; set; }
    /// A key stretching function, typically used for password hashing
    public required Ksf Ksf { get; set; }

    /// The default configuration for the OPAQUE protocol
    public static readonly CipherConfiguration Default = new CipherConfiguration
    {
        OpaqueVersion = 3,
        OprfCs = OprfCs.Ristretto255,
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
