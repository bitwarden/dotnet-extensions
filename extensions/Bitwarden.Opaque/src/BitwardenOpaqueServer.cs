namespace Bitwarden.Opaque;
#pragma warning disable CA1822 // Mark members as static


/// The result of <see cref="BitwardenOpaqueServer.StartRegistration"/>
public struct ServerRegistrationStartResult
{
    /// The registration response which is then passed to <see cref="BitwardenOpaqueClient.FinishRegistration"/>.
    public byte[] registrationResponse;
    /// The server setup, which needs to be persisted on the server for future logins.
    public byte[] serverSetup;
}

/// The result of <see cref="BitwardenOpaqueServer.FinishRegistration"/>
public struct ServerRegistrationFinishResult
{
    /// The server registration, which needs to be persisted on the server for future logins.
    public byte[] serverRegistration;
}

/// The result of <see cref="BitwardenOpaqueServer.StartLogin"/>
public struct ServerLoginStartResult
{
    /// The credential response which is then passed to <see cref="BitwardenOpaqueClient.FinishLogin"/>.
    public byte[] credentialResponse;
    /// The state generated during the login start, which needs to be stored until the login finish.
    public byte[] state;
}

/// The result of <see cref="BitwardenOpaqueServer.FinishLogin"/>
public struct ServerLoginFinishResult
{
    /// The session key generated after a successful login.
    public byte[] sessionKey;
}

/// A class to represent server side functionality the Bitwarden OPAQUE library.
public sealed partial class BitwardenOpaqueServer
{
    /// <summary>
    /// Start the server registration process. This must happen after <see cref="BitwardenOpaqueClient.StartRegistration"/> 
    /// </summary>
    /// <param name="config">The Cipher configuration, must be the same for all the operation</param>
    /// <param name="serverSetup">The server setup. Use null to let the library create a new random one</param>
    /// <param name="registrationRequest">The client registration request, <see cref="ClientRegistrationStartResult.registrationRequest"/> </param>
    /// <param name="username">The username to register</param>
    /// <returns></returns>
    public ServerRegistrationStartResult StartRegistration(CipherConfiguration config, byte[]? serverSetup, byte[] registrationRequest, string username)
    {
        var result = BitwardenLibrary.ExecuteFFIFunction((ffi) =>
        {
            return BitwardenLibrary.start_server_registration(ffi.Cfg(config), ffi.Buf(serverSetup), ffi.Buf(registrationRequest), username);
        }, 2);

        return new ServerRegistrationStartResult
        {
            registrationResponse = result[0],
            serverSetup = result[1]
        };
    }

    /// <summary>
    /// Finish the server registration process. This must happen after <see cref="BitwardenOpaqueClient.FinishRegistration"/> 
    /// </summary>
    /// <param name="config">The Cipher configuration, must be the same for all the operation</param>
    /// <param name="registrationUpload">The client registration upload, <see cref="ClientRegistrationFinishResult.registrationUpload"/> </param>
    /// <returns></returns>
    public ServerRegistrationFinishResult FinishRegistration(CipherConfiguration config, byte[] registrationUpload)
    {
        var result = BitwardenLibrary.ExecuteFFIFunction((ffi) =>
        {
            return BitwardenLibrary.finish_server_registration(ffi.Cfg(config), ffi.Buf(registrationUpload));
        }, 1);
        return new ServerRegistrationFinishResult
        {
            serverRegistration = result[0]
        };
    }

    /// <summary>
    /// Start the server login process. This must happen after <see cref="BitwardenOpaqueClient.StartLogin"/> 
    /// </summary>
    /// <param name="config">The Cipher configuration, must be the same for all the operation</param>
    /// <param name="serverSetup">The server setup, previously generated or supplied during registration</param>
    /// <param name="serverRegistration">The server registration, previously generated during registration</param>
    /// <param name="credentialRequest">The client credential request, <see cref="ClientLoginStartResult.credentialRequest"/> </param>
    /// <param name="username">The username to login</param>
    /// <returns></returns>
    public ServerLoginStartResult StartLogin(CipherConfiguration config, byte[] serverSetup, byte[] serverRegistration, byte[] credentialRequest, string username)
    {
        var result = BitwardenLibrary.ExecuteFFIFunction((ffi) =>
        {
            return BitwardenLibrary.start_server_login(ffi.Cfg(config), ffi.Buf(serverSetup), ffi.Buf(serverRegistration), ffi.Buf(credentialRequest), username);
        }, 2);
        return new ServerLoginStartResult
        {
            credentialResponse = result[0],
            state = result[1]
        };
    }

    /// <summary>
    /// Finish the server login process. This must happen after <see cref="BitwardenOpaqueClient.FinishLogin"/> 
    /// </summary>
    /// <param name="config">The Cipher configuration, must be the same for all the operation</param>
    /// <param name="state">The state generated during the login start, <see cref="ServerLoginStartResult.state"/> </param>
    /// <param name="credentialFinalization">The client credential finalization, <see cref="ClientLoginFinishResult.credentialFinalization"/> </param>
    /// <returns></returns>
    public ServerLoginFinishResult FinishLogin(CipherConfiguration config, byte[] state, byte[] credentialFinalization)
    {
        var result = BitwardenLibrary.ExecuteFFIFunction((ffi) =>
        {
            return BitwardenLibrary.finish_server_login(ffi.Cfg(config), ffi.Buf(state), ffi.Buf(credentialFinalization));
        }, 1);
        return new ServerLoginFinishResult
        {
            sessionKey = result[0]
        };
    }
    /// <summary>
    /// Generate a seeded fake registration. This can be returned for unenrolled users to avoid account enumeration issues.
    /// </summary>
    /// <param name="seed">The seed to use for the fake registration. This should be consistent between multiple calls to the same user</param>
    public (byte[] serverSetup, byte[] serverRegistration) SeededFakeRegistration(byte[] seed)
    {
        var result = BitwardenLibrary.ExecuteFFIFunction((ffi) =>
        {
            return BitwardenLibrary.register_seeded_fake_config(ffi.Buf(seed));
        }, 2);
        return (result[0], result[1]);
    }

}
