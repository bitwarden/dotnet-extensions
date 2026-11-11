using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Crypto.Tests;

// Exercises the full path: C# facade -> generated bindings -> native Rust library.
public class NativeServerSdkTests
{
    private readonly ServiceProvider _provider = new ServiceCollection()
        .AddServerSdkCrypto()
        .BuildServiceProvider();

    private ICryptoParsingClient Client => _provider.GetRequiredService<ICryptoParsingClient>();

    [Fact]
    public void Version_IsNotEmpty()
    {
        var info = _provider.GetRequiredService<IServerSdkInfo>();

        Assert.False(string.IsNullOrWhiteSpace(info.Version));
    }

    [Fact]
    public void AddServerSdkCrypto_SharesOneNativeClient()
    {
        var info = _provider.GetRequiredService<IServerSdkInfo>();

        Assert.Same(info, Client);
    }

    // Many threads on one instance: the Rust client is documented as thread-safe.
    [Fact]
    public void InspectEncString_Concurrent_ReturnsKeyId()
    {
        var results = new string?[1000];

        Parallel.For(0, results.Length, i => results[i] = Client.InspectEncString(Fixtures.CoseEncString).EncryptedByKeyId);

        Assert.All(results, id => Assert.Equal(Fixtures.KeyId, id));
    }
}
