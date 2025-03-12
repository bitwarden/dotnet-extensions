namespace Bitwarden.OPAQUE.Tests;

using Xunit;

public class SampleTests
{
    [Fact]
    public void TestRegistration()
    {
        // Get environment variables
        var username = "demo_username";
        var password = "demo_password";

        // Create the OPAQUE Clients
        var server = new BitwardenOpaqueServer();
        var client = new BitwardenOpaqueClient();

        var config = CipherConfiguration.Default;

        ///// Registration

        // Start the client registration
        var clientRegisterStartResult = client.StartRegistration(config, password);

        // Client sends reg_start to server
        var serverRegisterStartResult = server.StartRegistration(config, null, clientRegisterStartResult.registrationRequest, username);

        // Server sends server_start_result to client
        var clientRegisterFinishResult = client.FinishRegistration(config, clientRegisterStartResult.state, serverRegisterStartResult.registrationResponse, password);

        // Client sends client_finish_result to server
        var serverRegisterFinishResult = server.FinishRegistration(config, clientRegisterFinishResult.registrationUpload);

        Assert.NotNull(serverRegisterFinishResult.serverRegistration);

        ///// Login

        // Start the client login
        var clientLoginStartResult = client.StartLogin(config, password);

        // Client sends login_start to server
        var serverLoginStartResult = server.StartLogin(config, serverRegisterStartResult.serverSetup, serverRegisterFinishResult.serverRegistration, clientLoginStartResult.credentialRequest, username);

        // Server sends login_start_result to client
        var clientLoginFinishResult = client.FinishLogin(config, clientLoginStartResult.state, serverLoginStartResult.credentialResponse, password);

        // Client sends login_finish_result to server
        var serverLoginFinishResult = server.FinishLogin(config, serverLoginStartResult.state, clientLoginFinishResult.credentialFinalization);

        Assert.NotNull(serverLoginFinishResult.sessionKey);

    }
}
