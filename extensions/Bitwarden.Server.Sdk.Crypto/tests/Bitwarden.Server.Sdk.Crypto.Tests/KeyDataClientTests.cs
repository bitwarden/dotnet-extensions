using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Crypto.Tests;

// Exercises IKeyDataClient through the native library against data built with the SDK.
public class KeyDataClientTests
{
    private readonly ServiceProvider _provider = new ServiceCollection()
        .AddServerSdkCrypto()
        .BuildServiceProvider();

    private IKeyDataClient Client => _provider.GetRequiredService<IKeyDataClient>();

    [Fact]
    public void AddServerSdkCrypto_SharesOneNativeClient()
    {
        Assert.Same(Client, _provider.GetRequiredService<ICryptoParsingClient>());
    }

    [Fact]
    public void ValidateKey_PrivateKey_ReturnsIds()
    {
        var expected = new KeyInfo(
            KeyDataType.PrivateKey,
            new KeyIds(
                KeyDataFixtures.PublicKeyId, null, KeyDataFixtures.SealingKeyId, KeyDataFixtures.VerifyingKeyId));

        Assert.Equal(expected, Client.ValidateKey(KeyDataFixtures.PrivateKey));
    }

    [Fact]
    public void ValidateKey_SigningKey_ReturnsIds()
    {
        var expected = new KeyInfo(
            KeyDataType.SigningKey,
            new KeyIds(KeyDataFixtures.VerifyingKeyId, null, KeyDataFixtures.SealingKeyId, null));

        Assert.Equal(expected, Client.ValidateKey(KeyDataFixtures.SigningKey));
    }

    [Fact]
    public void ValidateKey_SymmetricKey_ReturnsIds()
    {
        var expected = new KeyInfo(
            KeyDataType.SymmetricKey,
            new KeyIds(null, KeyDataFixtures.SealedKeyId, KeyDataFixtures.SealingKeyId, null));

        Assert.Equal(expected, Client.ValidateKey(KeyDataFixtures.SymmetricKey));
    }

    [Fact]
    public void ValidateKey_SharedKey_ReturnsNoIds()
    {
        var expected = new KeyInfo(KeyDataType.SharedKey, new KeyIds(null, null, null, null));

        Assert.Equal(expected, Client.ValidateKey(KeyDataFixtures.SharedKey));
    }

    [Fact]
    public void ValidateKey_SecurityState_ReturnsSigner()
    {
        var expected = new KeyInfo(
            KeyDataType.SecurityState, new KeyIds(null, null, null, KeyDataFixtures.VerifyingKeyId));

        Assert.Equal(expected, Client.ValidateKey(KeyDataFixtures.SecurityState));
    }

    // The envelope names the symmetric key it holds, not the pair's private key.
    [Fact]
    public void ValidateKey_EnvelopeHoldsAnotherKey_ThrowsKeyIdMismatch()
    {
        var data = With(KeyDataFixtures.PrivateKey, "wrappedKey", KeyDataFixtures.Envelope);

        var ex = Assert.Throws<InvalidKeyDataException>(() => Client.ValidateKey(data));

        Assert.Equal(KeyDataError.KeyIdMismatch, ex.Error);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"type":"unknown","wrappedKey":"x"}""")]
    [InlineData("""{"type":"unsigned-shared-key","wrappedKey":"garbage"}""")]
    public void ValidateKey_Invalid_ThrowsInvalidData(string data)
    {
        var ex = Assert.Throws<InvalidKeyDataException>(() => Client.ValidateKey(data));

        Assert.Equal(KeyDataError.InvalidData, ex.Error);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void ValidateKey_UnknownMember_ThrowsInvalidData()
    {
        var data = With(KeyDataFixtures.SigningKey, "extra", "x");

        var ex = Assert.Throws<InvalidKeyDataException>(() => Client.ValidateKey(data));

        Assert.Equal(KeyDataError.InvalidData, ex.Error);
    }

    [Fact]
    public void ValidateUnlockMethod_MasterPassword_ReturnsIds()
    {
        var expected = new UnlockMethodInfo(
            UnlockMethodKind.MasterPassword,
            new KeyIds(null, KeyDataFixtures.SealedKeyId, KeyDataFixtures.SealingKeyId, null));

        Assert.Equal(expected, Client.ValidateUnlockMethod(KeyDataFixtures.MasterPassword));
    }

    [Fact]
    public void ValidateUnlockMethod_Device_ReturnsNoIds()
    {
        var expected = new UnlockMethodInfo(UnlockMethodKind.Device, new KeyIds(null, null, null, null));

        Assert.Equal(expected, Client.ValidateUnlockMethod(KeyDataFixtures.Device));
    }

    [Theory]
    [InlineData("salt", "")]
    [InlineData("memory", 15)]
    public void ValidateUnlockMethod_WeakKdf_ThrowsInvalidKdf(string member, object value)
    {
        var data = JsonNode.Parse(KeyDataFixtures.MasterPassword)!;
        data["kdf"]![member] = JsonValue.Create(value);

        var ex = Assert.Throws<InvalidKeyDataException>(() => Client.ValidateUnlockMethod(data.ToJsonString()));

        Assert.Equal(KeyDataError.InvalidKdf, ex.Error);
    }

    [Fact]
    public void ValidateUnlockMethod_NoKind_ThrowsInvalidData()
    {
        var data = JsonNode.Parse(KeyDataFixtures.Device)!.AsObject();
        data.Remove("kind");

        var ex = Assert.Throws<InvalidKeyDataException>(() => Client.ValidateUnlockMethod(data.ToJsonString()));

        Assert.Equal(KeyDataError.InvalidData, ex.Error);
    }

    [Fact]
    public void ValidateKey_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Client.ValidateKey(null!));
    }

    // data with member set to value.
    private static string With(string data, string member, string value)
    {
        var node = JsonNode.Parse(data)!;
        node[member] = value;

        return node.ToJsonString();
    }
}
