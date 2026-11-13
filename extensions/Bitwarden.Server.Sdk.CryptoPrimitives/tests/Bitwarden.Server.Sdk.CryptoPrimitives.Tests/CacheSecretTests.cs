namespace Bitwarden.Server.Sdk.CryptoPrimitives.Tests;

public sealed class CacheSecretTests
{
    // 00 01 02 ... 1f
    private static readonly byte[] SequentialBytes = [.. Enumerable.Range(0, CacheSecret.Length).Select(i => (byte)i)];
    private const string SequentialBase64 = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    [Fact]
    public void Development_IsDefaultAndAllZeros()
    {
        Assert.Equal(default, CacheSecret.Development);
        Assert.Equal(new byte[CacheSecret.Length], CacheSecret.Development.AsSpan().ToArray());
    }

    [Fact]
    public void FromString_DecodesBase64()
    {
        Assert.Equal(new CacheSecret(SequentialBytes), CacheSecret.FromString(SequentialBase64));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHg==")]
    [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8g")]
    public void FromString_NotBase64OfASecret_Throws(string value)
    {
        Assert.Throws<FormatException>(() => CacheSecret.FromString(value));
    }

    [Fact]
    public void FromString_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CacheSecret.FromString(null!));
    }

    [Fact]
    public void ToString_RoundTripsThroughFromString()
    {
        var secret = new CacheSecret(SequentialBytes);

        Assert.Equal(SequentialBase64, secret.ToString());
        Assert.Equal(secret, CacheSecret.FromString(secret.ToString()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(CacheSecret.Length - 1)]
    [InlineData(CacheSecret.Length + 1)]
    public void Constructor_WrongLength_Throws(int length)
    {
        Assert.Throws<ArgumentException>(() => new CacheSecret(new byte[length]));
    }

    [Fact]
    public void Constructor_CopiesBytes()
    {
        var bytes = new byte[CacheSecret.Length];
        var secret = new CacheSecret(bytes);

        bytes[0] = 1;

        Assert.Equal(CacheSecret.Development, secret);
    }

    [Fact]
    public void Equality_ComparesBytes()
    {
        var secret = new CacheSecret(SequentialBytes);
        var same = new CacheSecret(SequentialBytes);

        Assert.True(same == secret);
        Assert.False(same != secret);
        Assert.True(same.Equals((object)secret));
        Assert.Equal(secret.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(CacheSecret.Development, secret);
    }
}
