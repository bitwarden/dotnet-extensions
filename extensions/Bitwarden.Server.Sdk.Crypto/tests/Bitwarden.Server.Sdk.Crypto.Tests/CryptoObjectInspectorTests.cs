using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Crypto.Tests;

// Exercises ICryptoObjectInspector through the native library against sdk-internal test vectors.
public class CryptoObjectInspectorTests
{
    private const string Garbage = "garbage";

    // Valid base64, but not the expected structure.
    private const string NotCose = "AAAA";

    private readonly ServiceProvider _provider = new ServiceCollection()
        .AddServerSdkCrypto()
        .BuildServiceProvider();

    private ICryptoObjectInspector Inspector => _provider.GetRequiredService<ICryptoObjectInspector>();

    [Fact]
    public void AddServerSdkCrypto_SharesInspector()
    {
        var reader = _provider.GetRequiredService<IEncryptionKeyIdReader>();

        Assert.Same(reader, Inspector);
    }

    [Fact]
    public void InspectEncString_Cose_ReturnsKeyId()
    {
        var expected = new EncStringInfo(EncStringAlgorithm.XAes256Gcm, Fixtures.KeyId);

        Assert.Equal(expected, Inspector.InspectEncString(Fixtures.CoseEncString));
    }

    [Fact]
    public void InspectEncString_Legacy_ReturnsNoKeyId()
    {
        var expected = new EncStringInfo(EncStringAlgorithm.Aes256CbcHmac, null);

        Assert.Equal(expected, Inspector.InspectEncString(Fixtures.LegacyEncString));
    }

    // An UnsignedSharedKey is not an EncString.
    [Fact]
    public void InspectEncString_UnsignedSharedKey_Throws()
    {
        Assert.Throws<InvalidEncryptedDataException>(
            () => Inspector.InspectEncString(InspectorFixtures.UnsignedSharedKey));
    }

    [Fact]
    public void InspectEncString_Invalid_Throws()
    {
        var ex = Assert.Throws<InvalidEncryptedDataException>(() => Inspector.InspectEncString(Garbage));

        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void InspectUnsignedSharedKey_ReturnsAlgorithm()
    {
        var expected = new UnsignedSharedKeyInfo(UnsignedSharedKeyAlgorithm.Rsa2048OaepSha1);

        Assert.Equal(expected, Inspector.InspectUnsignedSharedKey(InspectorFixtures.UnsignedSharedKey));
    }

    // A symmetric EncString is valid, but not an UnsignedSharedKey.
    [Fact]
    public void InspectUnsignedSharedKey_Symmetric_Throws()
    {
        Assert.Throws<InvalidEncryptedDataException>(
            () => Inspector.InspectUnsignedSharedKey(Fixtures.CoseEncString));
    }

    [Fact]
    public void InspectSymmetricKeyEnvelope_ReturnsKeyIds()
    {
        var expected = new CryptoObjectKeyIds(Fixtures.KeyId, InspectorFixtures.EnvelopeSealingKeyId, null);

        Assert.Equal(expected, Inspector.InspectSymmetricKeyEnvelope(Fixtures.Envelope));
    }

    [Fact]
    public void InspectSymmetricKeyEnvelope_Invalid_Throws()
    {
        Assert.Throws<InvalidEncryptedDataException>(
            () => Inspector.InspectSymmetricKeyEnvelope(Fixtures.CoseEncString));
    }

    [Fact]
    public void InspectLegacyCompat_Envelope_ReturnsKeyIds()
    {
        var expected = new CryptoObjectKeyIds(
            InspectorFixtures.LegacyCompatEnvelopeSealedKeyId,
            InspectorFixtures.LegacyCompatEnvelopeSealingKeyId,
            null);

        var actual = Inspector.InspectLegacyCompatSymmetricKeyEnvelope(InspectorFixtures.LegacyCompatEnvelope);

        Assert.Equal(expected, actual);
    }

    // The EncString form names no sealed key.
    [Fact]
    public void InspectLegacyCompat_EncString_ReturnsSealingKey()
    {
        var expected = new CryptoObjectKeyIds(null, InspectorFixtures.LegacyCompatEncStringSealingKeyId, null);

        var actual = Inspector.InspectLegacyCompatSymmetricKeyEnvelope(InspectorFixtures.LegacyCompatEncString);

        Assert.Equal(expected, actual);
    }

    // Contains the EncString separator, so parsed as an EncString, but malformed.
    [Fact]
    public void InspectLegacyCompat_Invalid_Throws()
    {
        Assert.Throws<InvalidEncryptedDataException>(
            () => Inspector.InspectLegacyCompatSymmetricKeyEnvelope("2.AAAA|AAAA"));
    }

    [Fact]
    public void InspectPasswordProtected_ReturnsNoKeyIds()
    {
        var expected = new CryptoObjectKeyIds(null, null, null);

        var actual = Inspector.InspectPasswordProtectedKeyEnvelope(InspectorFixtures.PasswordProtectedEnvelope);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void InspectPasswordProtected_Invalid_Throws()
    {
        Assert.Throws<InvalidEncryptedDataException>(
            () => Inspector.InspectPasswordProtectedKeyEnvelope(Fixtures.Envelope));
    }

    [Fact]
    public void InspectDataEnvelope_ReturnsKeyId()
    {
        var expected = new CryptoObjectKeyIds(null, InspectorFixtures.DataEnvelopeKeyId, null);

        Assert.Equal(expected, Inspector.InspectDataEnvelope(InspectorFixtures.DataEnvelope));
    }

    [Fact]
    public void InspectDataEnvelope_Invalid_Throws()
    {
        Assert.Throws<InvalidEncryptedDataException>(() => Inspector.InspectDataEnvelope(NotCose));
    }

    [Fact]
    public void InspectSignedObject_ReturnsSigner()
    {
        var expected = new CryptoObjectKeyIds(null, null, InspectorFixtures.VerifyingKeyId);

        Assert.Equal(expected, Inspector.InspectSignedObject(InspectorFixtures.SignedObject));
    }

    [Fact]
    public void InspectSignedObject_Invalid_Throws()
    {
        Assert.Throws<InvalidEncryptedDataException>(() => Inspector.InspectSignedObject(NotCose));
    }

    [Fact]
    public void InspectVerifyingKey_ReturnsAlgorithmAndKeyId()
    {
        var expected = new VerifyingKeyInfo(VerifyingKeyAlgorithm.Ed25519, InspectorFixtures.VerifyingKeyId);

        Assert.Equal(expected, Inspector.InspectVerifyingKey(InspectorFixtures.VerifyingKey));
    }

    [Fact]
    public void InspectVerifyingKey_Invalid_Throws()
    {
        Assert.Throws<InvalidEncryptedDataException>(() => Inspector.InspectVerifyingKey(NotCose));
    }

    [Fact]
    public void InspectPublicKey_ReturnsAlgorithmAndKeyId()
    {
        var expected = new PublicKeyInfo(PublicKeyAlgorithm.RsaOaepSha1, InspectorFixtures.PublicKeyId);

        Assert.Equal(expected, Inspector.InspectPublicKey(InspectorFixtures.PublicKey));
    }

    [Fact]
    public void InspectPublicKey_Invalid_Throws()
    {
        Assert.Throws<InvalidEncryptedDataException>(() => Inspector.InspectPublicKey(NotCose));
    }

    [Fact]
    public void Inspect_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Inspector.InspectDataEnvelope(null!));
    }
}
