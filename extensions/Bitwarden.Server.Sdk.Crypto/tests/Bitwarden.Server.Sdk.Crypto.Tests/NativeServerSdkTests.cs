using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Crypto.Tests;

// Exercises the full path: C# facade -> generated bindings -> native Rust library.
public class NativeServerSdkTests
{
    private readonly ServiceProvider _provider = new ServiceCollection()
        .AddServerSdkCrypto()
        .BuildServiceProvider();

    private IEncryptionKeyIdReader Reader => _provider.GetRequiredService<IEncryptionKeyIdReader>();

    [Fact]
    public void Version_IsNotEmpty()
    {
        var info = _provider.GetRequiredService<IServerSdkInfo>();

        Assert.False(string.IsNullOrWhiteSpace(info.Version));
    }

    [Fact]
    public void FromEncString_Cose_ReturnsKeyId()
    {
        Assert.Equal(Fixtures.KeyId, Reader.FromEncString(Fixtures.CoseEncString));
    }

    [Fact]
    public void FromEncString_Legacy_ReturnsNull()
    {
        Assert.Null(Reader.FromEncString(Fixtures.LegacyEncString));
    }

    [Fact]
    public void FromEncString_Invalid_Throws()
    {
        var ex = Assert.Throws<InvalidEncryptedDataException>(() => Reader.FromEncString("garbage"));

        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void FromSymmetricKeyEnvelope_ReturnsKeyId()
    {
        Assert.Equal(InspectorFixtures.EnvelopeSealingKeyId, Reader.FromSymmetricKeyEnvelope(Fixtures.Envelope));
    }

    [Fact]
    public void FromSymmetricKeyEnvelope_Invalid_Throws()
    {
        Assert.Throws<InvalidEncryptedDataException>(() => Reader.FromSymmetricKeyEnvelope("garbage"));
    }

    [Fact]
    public void AddServerSdkCrypto_SharesOneNativeClient()
    {
        var info = _provider.GetRequiredService<IServerSdkInfo>();

        Assert.Same(info, Reader);
    }

    // Many threads on one instance: the Rust client is documented as thread-safe.
    [Fact]
    public void FromEncString_Concurrent_ReturnsKeyId()
    {
        var results = new string?[1000];

        Parallel.For(0, results.Length, i => results[i] = Reader.FromEncString(Fixtures.CoseEncString));

        Assert.All(results, id => Assert.Equal(Fixtures.KeyId, id));
    }
}
