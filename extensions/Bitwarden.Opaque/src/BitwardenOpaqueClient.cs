namespace Bitwarden.Opaque;
#pragma warning disable CA1822 // Mark members as static

/// The result of <see cref="BitwardenOpaqueClient.StartRegistration"/>
public struct ClientRegistrationStartResult
{
    /// The registration response which is then passed to <see cref="BitwardenOpaqueServer.StartRegistration"/>.
    public byte[] registrationRequest;

    /// The client state, which must be kept on the client for <see cref="BitwardenOpaqueClient.FinishRegistration"/>.
    public byte[] state;
}

/// The result of <see cref="BitwardenOpaqueClient.FinishRegistration"/>
public struct ClientRegistrationFinishResult
{
    /// The registration upload which is then passed to <see cref="BitwardenOpaqueServer.FinishRegistration"/>.
    public byte[] registrationUpload;
    /// The export key output by client registration
    public byte[] exportKey;
    /// The server's static public key
    public byte[] serverSPKey;
}

/// The result of <see cref="BitwardenOpaqueClient.StartLogin"/>
public struct ClientLoginStartResult
{
    /// The credential request which is then passed to <see cref="BitwardenOpaqueServer.StartLogin"/>.
    public byte[] credentialRequest;
    /// The state generated during the login start, which must be kept on the client for <see cref="BitwardenOpaqueClient.FinishLogin"/>.
    public byte[] state;
}

/// The result of <see cref="BitwardenOpaqueClient.FinishLogin"/>
public struct ClientLoginFinishResult
{
    /// The credential finalization which is then passed to <see cref="BitwardenOpaqueServer.FinishLogin"/>.
    public byte[] credentialFinalization;
    /// The session key generated after a successful login.
    public byte[] sessionKey;
    /// The export key output by client login.
    public byte[] exportKey;
    /// The server's static public key.
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
        var result = BitwardenLibrary.ExecuteFFIFunction((ffi) =>
        {
            return BitwardenLibrary.start_client_registration(ffi.Cfg(config), password);
        }, 2);
        return new ClientRegistrationStartResult
        {
            registrationRequest = result[0],
            state = result[1]
        };
    }

    /// <summary>
    /// Finish the server registration process. This must happen after <see cref="BitwardenOpaqueServer.StartRegistration"/> 
    /// </summary>
    /// <param name="config">The Cipher configuration, must be the same for all the operation</param>
    /// <param name="state">The state obtained from the client start operation, <see cref="ClientRegistrationStartResult.state"/> </param>
    /// <param name="registrationResponse">The server registration response, <see cref="ServerRegistrationStartResult.registrationResponse"/> </param>
    /// <param name="password">The password to register</param>
    /// <returns></returns>
    public ClientRegistrationFinishResult FinishRegistration(CipherConfiguration config, byte[] state, byte[] registrationResponse, string password)
    {
        var result = BitwardenLibrary.ExecuteFFIFunction((ffi) =>
        {
            return BitwardenLibrary.finish_client_registration(ffi.Cfg(config), ffi.Buf(state), ffi.Buf(registrationResponse), password);
        }, 3);
        return new ClientRegistrationFinishResult
        {
            registrationUpload = result[0],
            exportKey = result[1],
            serverSPKey = result[2]
        };
    }

    /// <summary>
    /// Start the client login process. This is the first step in the login process.
    /// </summary>
    /// <param name="config">The Cipher configuration, must be the same for all the operation</param>
    /// <param name="password">The password to login</param>
    /// <returns></returns>
    public ClientLoginStartResult StartLogin(CipherConfiguration config, string password)
    {
        var result = BitwardenLibrary.ExecuteFFIFunction((ffi) =>
        {
            return BitwardenLibrary.start_client_login(ffi.Cfg(config), password);
        }, 2);
        return new ClientLoginStartResult
        {
            credentialRequest = result[0],
            state = result[1]
        };

    }

    /// <summary>
    /// Finish the client login process. This must happen after <see cref="BitwardenOpaqueServer.StartLogin"/> 
    /// </summary>
    /// <param name="config">The Cipher configuration, must be the same for all the operation</param>
    /// <param name="state">The state obtained from the client start operation, <see cref="ClientLoginStartResult.state"/> </param>
    /// <param name="credentialResponse">The server credential response, <see cref="ServerLoginStartResult.credentialResponse"/> </param>
    /// <param name="password">The password to login</param>
    /// <returns></returns>
    public ClientLoginFinishResult FinishLogin(CipherConfiguration config, byte[] state, byte[] credentialResponse, string password)
    {
        var result = BitwardenLibrary.ExecuteFFIFunction((ffi) =>
        {
            return BitwardenLibrary.finish_client_login(ffi.Cfg(config), ffi.Buf(state), ffi.Buf(credentialResponse), password);
        }, 4);
        return new ClientLoginFinishResult
        {
            credentialFinalization = result[0],
            sessionKey = result[1],
            exportKey = result[2],
            serverSPKey = result[3]
        };
    }
}
