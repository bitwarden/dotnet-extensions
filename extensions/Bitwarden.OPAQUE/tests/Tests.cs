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

        // Start the client registration
        var clientStartResult = client.StartRegistration(config, password);

        // Client sends reg_start to server
        var serverStartResult = server.StartRegistration(config, clientStartResult.registrationRequest, username);

        // Server sends server_start_result to client
        var clientFinishResult = client.FinishRegistration(config, clientStartResult.state, serverStartResult.registrationResponse, password);

        // Client sends client_finish_result to server
        var serverFinishResult = server.FinishRegistration(config, clientFinishResult.registrationUpload);

        Assert.NotNull(serverFinishResult.serverRegistration);
    }
}
