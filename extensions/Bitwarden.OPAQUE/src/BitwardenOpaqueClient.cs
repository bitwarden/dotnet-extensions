namespace Bitwarden.OPAQUE;
#pragma warning disable CA1822 // Mark members as static

/// The result of <see cref="Bitwarden.OPAQUE.BitwardenOpaqueClient.StartRegistration"/>
public struct ClientRegistrationStartResult
{
    /// The registration response which is then passed to <see cref="Bitwarden.OPAQUE.BitwardenOpaqueServer.StartRegistration"/>.
    public byte[] registrationRequest;

    /// The client state, which must be kept on the client for <see cref="Bitwarden.OPAQUE.BitwardenOpaqueClient.FinishRegistration"/>.
    public byte[] state;
}

/// The result of <see cref="Bitwarden.OPAQUE.BitwardenOpaqueClient.FinishRegistration"/>
public struct ClientRegistrationFinishResult
{
    /// The registration upload which is then passed to <see cref="Bitwarden.OPAQUE.BitwardenOpaqueServer.FinishRegistration"/>.
    public byte[] registrationUpload;
    /// The export key output by client registration
    public byte[] exportKey;
    /// The server's static public key
    public byte[] serverSPKey;
}

public struct ClientLoginStartResult
{
    public byte[] credentialRequest;
    public byte[] state;
}

public struct ClientLoginFinishResult
{
    public byte[] credentialFinalization;
    public byte[] sessionKey;
    public byte[] exportKey;
    public byte[] serverSPKey;
}

/// A class to represent client side functionality the Bitwarden OPAQUE library.
public sealed partial class BitwardenOpaqueClient
{

    /// <summary>
    /// Start the client registration process. This is the first step in the registration process.
    /// </summary>
    /// <param name="config">The Cipher configuration, must be the same for all the operation</param>
    /// <param name="password">The password to register</param>
    /// <returns></returns>
    public ClientRegistrationStartResult StartRegistration(CipherConfiguration config, string password)
    {
        var (registrationRequest, state) = BitwardenLibrary.StartClientRegistration(config, password);
        return new ClientRegistrationStartResult
        {
            registrationRequest = registrationRequest,
            state = state
        };
    }

    /// <summary>
    /// Finish the server registration process. This must happen after <see cref="Bitwarden.OPAQUE.BitwardenOpaqueServer.StartRegistration"/> 
    /// </summary>
    /// <param name="config">The Cipher configuration, must be the same for all the operation</param>
    /// <param name="state">The state obtained from the client start operation, <see cref="Bitwarden.OPAQUE.ClientRegistrationStartResult.state"/> </param>
    /// <param name="registrationResponse">The server registration response, <see cref="Bitwarden.OPAQUE.ServerRegistrationStartResult.registrationResponse"/> </param>
    /// <param name="password">The password to register</param>
    /// <returns></returns>
    public ClientRegistrationFinishResult FinishRegistration(CipherConfiguration config, byte[] state, byte[] registrationResponse, string password)
    {
        var (registrationUpload, exportKey, serverSPKey) = BitwardenLibrary.FinishClientRegistration(config, state, registrationResponse, password);
        return new ClientRegistrationFinishResult
        {
            registrationUpload = registrationUpload,
            exportKey = exportKey,
            serverSPKey = serverSPKey
        };
    }


    public ClientLoginStartResult StartLogin(CipherConfiguration config, string password)
    {
        var (credentialRequest, state) = BitwardenLibrary.StartClientLogin(config, password);
        return new ClientLoginStartResult
        {
            credentialRequest = credentialRequest,
            state = state
        };

    }

    public ClientLoginFinishResult FinishLogin(CipherConfiguration config, byte[] state, byte[] credentialResponse, string password)
    {
        var (credentialFinalization, sessionKey, exportKey, serverSPKey) = BitwardenLibrary.FinishClientLogin(config, state, credentialResponse, password);
        return new ClientLoginFinishResult
        {
            credentialFinalization = credentialFinalization,
            sessionKey = sessionKey,
            exportKey = exportKey,
            serverSPKey = serverSPKey
        };
    }
}
