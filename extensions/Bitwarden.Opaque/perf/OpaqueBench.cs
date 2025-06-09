using BenchmarkDotNet.Attributes;

namespace Bitwarden.Opaque.Benchmarks;

// dotnet run --project extensions/Bitwarden.Opaque/perf/Bitwarden.Opaque.Benchmarks.csproj -c Release -p:BuildOpaqueLib=true

[MemoryDiagnoser]
public class OpaqueBench
{
    public BitwardenOpaqueServer server = new();
    public BitwardenOpaqueClient client = new();
    public CipherConfiguration config = CipherConfiguration.Default;

    public string username = "demo_username";
    public string password = "demo_password";

    public byte[] serverSetup = null!;
    public byte[] serverRegistration = null!;

    public byte[] clientRegistrationRequest = null!;
    public byte[] clientRegistrationUpload = null!;

    public byte[] clientLoginCredentialRequest = null!;
    public byte[] serverLoginState = null!;
    public byte[] clientLoginCredentialFinalization = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Use the complete benchmarks to extract the data for the partial benchmarks
        var registration = CompleteRegistration();
        serverSetup = registration.Item1;
        serverRegistration = registration.Item2;
        clientRegistrationRequest = registration.Item3;
        clientRegistrationUpload = registration.Item4;

        var login = CompleteLogin();
        clientLoginCredentialRequest = login.Item1;
        serverLoginState = login.Item2;
        clientLoginCredentialFinalization = login.Item3;
    }

    [Benchmark]
    public (byte[], byte[]) SeededFakeRegistration()
    {
        var seed = new byte[32];
        return server.SeededFakeRegistration(seed);
    }

    [Benchmark]
    public (byte[], byte[], byte[], byte[]) CompleteRegistration()
    {
        var clientRegisterStartResult = client.StartRegistration(config, password);
        var serverRegisterStartResult = server.StartRegistration(config, null, clientRegisterStartResult.registrationRequest, username);
        var clientRegisterFinishResult = client.FinishRegistration(config, clientRegisterStartResult.state, serverRegisterStartResult.registrationResponse, password);
        var serverRegisterFinishResult = server.FinishRegistration(config, clientRegisterFinishResult.registrationUpload);
        return (
            serverRegisterStartResult.serverSetup,
            serverRegisterFinishResult.serverRegistration,
            clientRegisterStartResult.registrationRequest,
            clientRegisterFinishResult.registrationUpload
        );
    }

    [Benchmark]
    public (byte[], byte[], byte[], byte[]) CompleteLogin()
    {
        var clientLoginStartResult = client.StartLogin(config, password);
        var serverLoginStartResult = server.StartLogin(config, serverSetup, serverRegistration, clientLoginStartResult.credentialRequest, username);
        var clientLoginFinishResult = client.FinishLogin(config, clientLoginStartResult.state, serverLoginStartResult.credentialResponse, password);
        var serverLoginFinishResult = server.FinishLogin(config, serverLoginStartResult.state, clientLoginFinishResult.credentialFinalization);
        return (
            clientLoginStartResult.credentialRequest,
            serverLoginStartResult.state,
            clientLoginFinishResult.credentialFinalization,
            serverLoginFinishResult.sessionKey
        );
    }

    [Benchmark]
    public (byte[], byte[]) StartServerRegistration()
    {
        var result = server.StartRegistration(config, null, clientRegistrationRequest, username);
        return (result.registrationResponse, result.serverSetup);
    }

    [Benchmark]
    public byte[] FinishServerRegistration()
    {
        var result = server.FinishRegistration(config, clientRegistrationUpload);
        return result.serverRegistration;
    }


    [Benchmark]
    public (byte[], byte[]) StartServerLogin()
    {
        var result = server.StartLogin(config, serverSetup, serverRegistration, clientLoginCredentialRequest, username);
        return (result.credentialResponse, result.state);
    }

    [Benchmark]
    public byte[] FinishServerLogin()
    {
        var result = server.FinishLogin(config, serverLoginState, clientLoginCredentialFinalization);
        return result.sessionKey;
    }
}
