namespace Bitwarden.Opaque;
#pragma warning disable CA1822 // Mark members as static


/// The result of <see cref="Bitwarden.Opaque.BitwardenOpaqueServer.StartRegistration"/>
public struct ServerRegistrationStartResult
{
    /// The registration response which is then passed to <see cref="Bitwarden.Opaque.BitwardenOpaqueClient.FinishRegistration"/>.
    public byte[] registrationResponse;
    /// The server setup, which needs to be persisted on the server for future logins.
    public byte[] serverSetup;
}

/// The result of <see cref="Bitwarden.Opaque.BitwardenOpaqueServer.FinishRegistration"/>
public struct ServerRegistrationFinishResult

{
    /// The server registration, which needs to be persisted on the server for future logins.
    public byte[] serverRegistration;
}

public struct ServerLoginStartResult
{
    public byte[] credentialResponse;
    public byte[] state;
}

public struct ServerLoginFinishResult
{
    public byte[] sessionKey;

}

/// A class to represent server side functionality the Bitwarden OPAQUE library.
public sealed partial class BitwardenOpaqueServer
{
    /// <summary>
    /// Start the server registration process. This must happen after <see cref="Bitwarden.Opaque.BitwardenOpaqueClient.StartRegistration"/> 
    /// </summary>
    /// <param name="config">The Cipher configuration, must be the same for all the operation</param>
    /// <param name="serverSetup">The server setup. Use null to let the library create a new random one</param>
    /// <param name="registrationRequest">The client registration request, <see cref="Bitwarden.Opaque.ClientRegistrationStartResult.registrationRequest"/> </param>
    /// <param name="username">The username to register</param>
    /// <returns></returns>
    public ServerRegistrationStartResult StartRegistration(CipherConfiguration config, byte[]? serverSetup, byte[] registrationRequest, string username)
    {
        var (registrationResponse, serverSetupRet) = BitwardenLibrary.StartServerRegistration(config, serverSetup, registrationRequest, username);
        return new ServerRegistrationStartResult
        {
            registrationResponse = registrationResponse,
            serverSetup = serverSetupRet
        };
    }

    /// <summary>
    /// Finish the server registration process. This must happen after <see cref="Bitwarden.Opaque.BitwardenOpaqueClient.FinishRegistration"/> 
    /// </summary>
    /// <param name="config">The Cipher configuration, must be the same for all the operation</param>
    /// <param name="registrationUpload">The client registration upload, <see cref="Bitwarden.Opaque.ClientRegistrationFinishResult.registrationUpload"/> </param>
    /// <returns></returns>
    public ServerRegistrationFinishResult FinishRegistration(CipherConfiguration config, byte[] registrationUpload)
    {
        var serverRegistration = BitwardenLibrary.FinishServerRegistration(config, registrationUpload);
        return new ServerRegistrationFinishResult
        {
            serverRegistration = serverRegistration
        };
    }

    public ServerLoginStartResult StartLogin(CipherConfiguration config, byte[] serverSetup, byte[] serverRegistration, byte[] credentialRequest, string username)
    {
        var (credentialResponse, state) = BitwardenLibrary.StartServerLogin(config, serverSetup, serverRegistration, credentialRequest, username);
        return new ServerLoginStartResult
        {
            credentialResponse = credentialResponse,
            state = state
        };
    }

    public ServerLoginFinishResult FinishLogin(CipherConfiguration config, byte[] state, byte[] credentialFinalization)
    {
        var sessionKey = BitwardenLibrary.FinishServerLogin(config, state, credentialFinalization);
        return new ServerLoginFinishResult
        {
            sessionKey = sessionKey
        };
    }

}
