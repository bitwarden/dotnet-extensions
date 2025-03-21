namespace Bitwarden.Opaque.Tests;

using Xunit;

public class OpaqueTests
{
    // Lower the config values from default so the tests run fast
    public readonly CipherConfiguration config = new()
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
                Iterations = 1,
                Memory = 1024,
                Parallelism = 1
            }
        }
    };

    [Fact]
    public void TestSeededRegistration()
    {
        var server = new BitwardenOpaqueServer();

        var seed = new byte[32];
        var (serverSetup1, serverRegistration1) = server.SeededFakeRegistration(seed);

        Assert.NotNull(serverSetup1);
        Assert.NotNull(serverRegistration1);

        var (serverSetup2, serverRegistration2) = server.SeededFakeRegistration(seed);
        Assert.NotNull(serverSetup2);
        Assert.NotNull(serverRegistration2);

        Assert.Equal(serverSetup1, serverSetup2);
        Assert.Equal(serverRegistration1, serverRegistration2);
    }


    [Fact]
    public void TestRegistration()
    {
        var username = "demo_username";
        var password = "demo_password";

        // Create the OPAQUE Clients
        var server = new BitwardenOpaqueServer();
        var client = new BitwardenOpaqueClient();

        // Start the client registration
        var clientRegisterStartResult = client.StartRegistration(config, password);

        // Client sends reg_start to server
        var serverRegisterStartResult = server.StartRegistration(config, null, clientRegisterStartResult.registrationRequest, username);

        // Server sends server_start_result to client
        var clientRegisterFinishResult = client.FinishRegistration(config, clientRegisterStartResult.state, serverRegisterStartResult.registrationResponse, password);

        // Client sends client_finish_result to server
        var serverRegisterFinishResult = server.FinishRegistration(config, clientRegisterFinishResult.registrationUpload);

        // These two need to be stored in the server for future logins
        Assert.NotNull(serverRegisterStartResult.serverSetup);
        Assert.NotNull(serverRegisterFinishResult.serverRegistration);

        Assert.NotNull(clientRegisterFinishResult.exportKey);
        Assert.NotNull(clientRegisterFinishResult.serverSPKey);
    }
    [Fact]
    public void TestLogin()
    {
        var username = "demo_username";
        var password = "demo_password";

        // These values have been obtained from a previous registration with the same user/pass
        var serverSetup = Convert.FromBase64String("i1mHwGvcVd5iYedbbgYFnFNLOSbotw+Ltgvr+xkNaGp1exkmDOjmFlr5McxjGAff2zermIpPezwCzq1C95Tot+gKuJqwWJOJ6jMXIrg7dSx6+H1IvZnR7LFtI7ylYoMFTvOWPyMyfoPTHK/+IlzgB10bKYcuPb+W4vH224qrXAk=");
        var serverRegistration = Convert.FromBase64String("ECHaam+JiZMa+lO8Rn6f5G4polvgvi468qUy1i6IaSu2L0Rh7XiQ5hm3KSu9doCGKIgfgeju/A5i8aefKZvxPtduytVRtaJm57+5jX7YYW1lv53jDIrvdgDwBt/xBO8Sghm8yzo/BUDDcYvClRx1N7rqk9CfaSQxkKQwKvgFeDtiXVWDj0i7MvVs6bBAFq9fprI8ahfdfeiQWx1Qcx5itCx7hlnzzvL4XwnNc3otFtz60PnYVsUpO+Mbe86ZGrNX");
        var serverSPKey = Convert.FromBase64String("8C9MWibFiO5PSCDXGQc2/jxTLCGBv6PC0jje9BOUhmk=");

        byte[]? previousSessionKey = null;
        byte[]? previousExportKey = null;

        for (var i = 0; i < 2; i++)
        {
            // Create the OPAQUE Clients
            var server = new BitwardenOpaqueServer();
            var client = new BitwardenOpaqueClient();

            // Start the client login
            var clientLoginStartResult = client.StartLogin(config, password);

            // Client sends login_start to server
            var serverLoginStartResult = server.StartLogin(config, serverSetup, serverRegistration, clientLoginStartResult.credentialRequest, username);

            // Server sends login_start_result to client
            var clientLoginFinishResult = client.FinishLogin(config, clientLoginStartResult.state, serverLoginStartResult.credentialResponse, password);

            // Client sends login_finish_result to server
            var serverLoginFinishResult = server.FinishLogin(config, serverLoginStartResult.state, clientLoginFinishResult.credentialFinalization);

            // Session key must be the same in both client and server
            Assert.NotNull(serverLoginFinishResult.sessionKey);
            Assert.Equal(serverLoginFinishResult.sessionKey, clientLoginFinishResult.sessionKey);

            // SPKey must be the same as during registration
            Assert.Equal(clientLoginFinishResult.serverSPKey, serverSPKey);

            if (i == 0)
            {
                previousSessionKey = serverLoginFinishResult.sessionKey;
                previousExportKey = clientLoginFinishResult.exportKey;
            }
            else
            {
                // Session key must be different for each login
                Assert.NotNull(serverLoginFinishResult.sessionKey);
                Assert.NotEqual(previousSessionKey, serverLoginFinishResult.sessionKey);

                // Export key must be the same for all logins
                Assert.NotNull(clientLoginFinishResult.exportKey);
                Assert.Equal(previousExportKey, clientLoginFinishResult.exportKey);
            }
        }
    }
}
