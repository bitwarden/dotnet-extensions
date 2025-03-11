namespace Bitwarden.OPAQUE.Tests;

using Xunit;

public class SampleTests
{
    [Fact]
    public void RunSample_Works()
    {
        // Get environment variables
        var username = "demo_username";
        var password = "demo_password";

        // Create the OPAQUE Client
        var client = new BitwardenOpaque();

        var config = CipherConfiguration.Default;

        // Start the client registration
        var (clientRequest, clientState) = client.StartClientRegistration(config, password);

        // Client sends reg_start to server
        var (serverResponse, serverSetup) = client.StartServerRegistration(config, clientRequest, username);

        // Server sends server_start_result to client
        var (registrationUpload, exportKey, serverSPKey) = client.FinishClientRegistration(config, clientState, serverResponse, password);

        // Client sends client_finish_result to server
        var result = client.FinishServerRegistration(config, registrationUpload);

        Assert.NotNull(result);
    }
}
