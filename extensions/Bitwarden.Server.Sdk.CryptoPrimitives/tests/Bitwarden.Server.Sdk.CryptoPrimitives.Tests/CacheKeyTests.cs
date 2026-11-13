using System.Text;

namespace Bitwarden.Server.Sdk.CryptoPrimitives.Tests;

public sealed class CacheKeyTests
{
    // 00 01 02 ... 1f
    private static readonly CacheKey Sequential = new([.. Enumerable.Range(0, CacheKey.Length).Select(i => (byte)i)]);

    // Vectors computed independently with Python's hmac and hashlib:
    // hmac.new(key, b"".join(struct.pack(">Q", len(b)) + b for b in utf8_params), sha256)
    public static TheoryData<CacheKey, string[], string> Vectors => new()
    {
        { CacheKey.Development, [], "b613679a0814d9ec772f95d778c35fc5ff1697c493715653c6c712144292c5ad" },
        { CacheKey.Development, [""], "f375180aba92888401f1919be4a8715a62763b65c1c10e1d0858e81d4d6f9fd2" },
        { CacheKey.Development, ["a"], "df763263167c619bd7cdaebfcc476f1c83e99f3982c5c214c7fa677feda60292" },
        { CacheKey.Development, ["ab", "c"], "57cb2a935abcbdc6835412cfbedba101a4eda4ae3439c2d1921ecfb9dd9aa37d" },
        { CacheKey.Development, ["a", "bc"], "52ed968663a15827e3f497b4ffdfc2b4d77dbdec50718162153f43d3ce3adb9c" },
        { Sequential, ["account-by-email", "user@example.com"], "00f61fc849cd4b4e1478ad8120b66d80f2670a229d9c8cc8bd7508f86622af20" },
        { Sequential, ["grüße", "€", "\U0001F600"], "a7051cf162d9c5a5e633a1b5ce15a7c09d8d736647ac0148faaab04e5aa41255" },
    };

    [Theory]
    [MemberData(nameof(Vectors))]
    public void DeriveCacheKey_MatchesVector(CacheKey secret, string[] parameters, string expected)
    {
        var key = CacheKey.DeriveCacheKey(secret, parameters);

        Assert.Equal(expected, Convert.ToHexStringLower(key.AsSpan()));
    }

    [Fact]
    public void DeriveCacheKey_FromDerivedKey_MatchesVector()
    {
        var tenant = CacheKey.DeriveCacheKey(Sequential, "tenant");

        var key = CacheKey.DeriveCacheKey(tenant, "widget");

        Assert.Equal("efb7ba330a7b1ea7857b4886fbd1eff5be922fed857cfedd4c1982f0c3e3dc51", Convert.ToHexStringLower(key.AsSpan()));
    }

    [Fact]
    public void DeriveCacheKey_TrailingEmptyParameter_DiffersFromNone()
    {
        Assert.NotEqual(CacheKey.DeriveCacheKey(Sequential, "a"), CacheKey.DeriveCacheKey(Sequential, "a", ""));
    }

    [Fact]
    public void DeriveCacheKey_AnotherSecret_Differs()
    {
        Assert.NotEqual(CacheKey.DeriveCacheKey(Sequential, "a"), CacheKey.DeriveCacheKey(CacheKey.Development, "a"));
    }

    [Fact]
    public void DeriveCacheKey_NullParameter_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CacheKey.DeriveCacheKey(Sequential, "a", null!));
    }

    [Fact]
    public void DeriveCacheKey_LoneSurrogate_Throws()
    {
        // "\ud800" would otherwise encode as U+FFFD and collide with "�".
        Assert.Throws<EncoderFallbackException>(() => CacheKey.DeriveCacheKey(Sequential, "\ud800"));
    }

    [Fact]
    public void FromString_DecodesBase64()
    {
        // base64 of 00 01 02 ... 1f
        var key = CacheKey.FromString("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=");

        Assert.Equal(Sequential, key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHg==")]
    [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8g")]
    [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIj")]
    public void FromString_NotBase64OfAKey_Throws(string value)
    {
        Assert.Throws<FormatException>(() => CacheKey.FromString(value));
    }

    [Fact]
    public void FromString_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CacheKey.FromString(null!));
    }

    [Fact]
    public void Development_IsDefaultAndAllZeros()
    {
        Assert.Equal(default, CacheKey.Development);
        Assert.Equal(new byte[CacheKey.Length], CacheKey.Development.AsSpan().ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(CacheKey.Length - 1)]
    [InlineData(CacheKey.Length + 1)]
    public void Constructor_WrongLength_Throws(int length)
    {
        Assert.Throws<ArgumentException>(() => new CacheKey(new byte[length]));
    }

    [Fact]
    public void Constructor_CopiesBytes()
    {
        var bytes = new byte[CacheKey.Length];
        var key = new CacheKey(bytes);

        bytes[0] = 1;

        Assert.Equal(CacheKey.Development, key);
    }

    [Fact]
    public void Equality_ComparesBytes()
    {
        var same = new CacheKey(Sequential.AsSpan());

        Assert.True(same == Sequential);
        Assert.False(same != Sequential);
        Assert.True(same.Equals((object)Sequential));
        Assert.Equal(Sequential.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(CacheKey.Development, Sequential);
    }

    [Fact]
    public void ToString_IsBase64()
    {
        Assert.Equal("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=", Sequential.ToString());
    }

    [Fact]
    public void ToString_RoundTripsThroughFromString()
    {
        var key = CacheKey.DeriveCacheKey(Sequential, "account-by-email", "user@example.com");

        Assert.Equal(key, CacheKey.FromString(key.ToString()));
    }
}
