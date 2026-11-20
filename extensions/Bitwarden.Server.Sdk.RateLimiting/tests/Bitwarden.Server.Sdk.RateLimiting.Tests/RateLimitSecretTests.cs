using Bitwarden.Server.Sdk.CryptoPrimitives;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Bitwarden.Server.Sdk.RateLimiting.Tests;

/// <summary>
/// RateLimitSecret reads RateLimiting:Secret with the SDK's one rule (CacheSecretSetting, tested there); these check only what is its own.
/// </summary>
public sealed class RateLimitSecretTests
{
    private static readonly CacheSecret Secret = new(Enumerable.Repeat((byte)1, CacheSecret.Length).ToArray());

    // A wire-contract constant: a deployment sets it by this name.
    [Fact]
    public void SecretKey_IsRateLimitingSecret()
    {
        Assert.Equal("RateLimiting:Secret", RateLimitSecret.SecretKey);
    }

    [Fact]
    public void ReadSecret_ReadsItsOwnKey()
    {
        Assert.Equal(Secret, RateLimitSecret.ReadSecret(Configuration(Secret.ToString()), "Production"));
    }

    [Fact]
    public void ReadSecret_UnsetInDevelopment_IsTheDevelopmentSecret()
    {
        Assert.Equal(CacheSecret.Development, RateLimitSecret.ReadSecret(Configuration(null), "Development"));
    }

    [Fact]
    public void ReadSecret_UnsetOutsideDevelopment_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => RateLimitSecret.ReadSecret(Configuration(null), "Production"));

        Assert.Equal("RateLimiting:Secret must be set when counts are shared in Redis outside Development.", exception.Message);
    }

    private static IConfiguration Configuration(string? secret) => new SecretOnly(secret);

    private sealed class SecretOnly(string? secret) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == RateLimitSecret.SecretKey ? secret : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => throw new NotSupportedException();

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
