namespace Bitwarden.OPAQUE;

public enum OprfCS
{
    Ristretto255
}

public enum KeGroup
{
    Ristretto255
}

public enum KeyExchange
{
    TripleDH
}

public abstract record KSF;

public record Argon2id(int iterations, int memoryKiB, int parallelism) : KSF;

public struct CipherConfiguration
{
    public OprfCS OprfCS;
    public KeGroup KeGroup;
    public KeyExchange KeyExchange;
    public KSF KSF;

    public static readonly CipherConfiguration Default = new CipherConfiguration
    {
        OprfCS = OprfCS.Ristretto255,
        KeGroup = KeGroup.Ristretto255,
        KeyExchange = KeyExchange.TripleDH,
        KSF = new Argon2id(4, 65536, 4)
    };

}

public sealed partial class BitwardenOpaque
{


    public (byte[], byte[]) StartServerRegistration(CipherConfiguration config, byte[] requestBytes, string username)
    {
        return BitwardenLibrary.StartServerRegistration(requestBytes, username);
    }

    public byte[] FinishServerRegistration(CipherConfiguration config, byte[] registrationUploadBytes)
    {
        return BitwardenLibrary.FinishServerRegistration(registrationUploadBytes);
    }

    public (byte[], byte[]) StartClientRegistration(CipherConfiguration config, string password)
    {
        return BitwardenLibrary.StartClientRegistration(password);
    }

    public (byte[], byte[], byte[]) FinishClientRegistration(CipherConfiguration config, byte[] stateBytes, byte[] registrationResponseBytes, string password)
    {
        return BitwardenLibrary.FinishClientRegistration(stateBytes, registrationResponseBytes, password);
    }
}
