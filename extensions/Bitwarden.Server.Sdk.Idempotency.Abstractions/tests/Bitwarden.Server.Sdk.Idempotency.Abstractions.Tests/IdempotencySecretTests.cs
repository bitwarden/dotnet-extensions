using Bitwarden.Server.Sdk.CryptoPrimitives;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Bitwarden.Server.Sdk.Idempotency.Abstractions.Tests;

public sealed class IdempotencySecretTests
{
    private static readonly CacheSecret Secret = new(Enumerable.Repeat((byte)1, CacheSecret.Length).ToArray());

    // A wire-contract constant: a deployment sets it by this name.
    [Fact]
    public void SecretKey_IsIdempotencySecret()
    {
        Assert.Equal("Idempotency:Secret", IdempotencySecret.SecretKey);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    public void ReadSecret_UnsetInDevelopment_IsTheDevelopmentSecret(string environment)
    {
        Assert.Equal(CacheSecret.Development, IdempotencySecret.ReadSecret(Configuration(null), environment));
        Assert.Equal(CacheSecret.Development, IdempotencySecret.ReadSecret(Configuration(" "), environment));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Testing")]
    [InlineData("QA")]
    public void ReadSecret_UnsetOutsideDevelopment_Throws(string environment)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => IdempotencySecret.ReadSecret(Configuration(null), environment));

        Assert.Contains(IdempotencySecret.SecretKey, exception.Message);
    }

    [Fact]
    public void ReadSecret_TheDevelopmentSecretOutsideDevelopment_Throws()
    {
        var value = CacheSecret.Development.ToString();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            IdempotencySecret.ReadSecret(Configuration(value), "Production"));

        Assert.Contains(IdempotencySecret.SecretKey, exception.Message);
        Assert.DoesNotContain(value, exception.Message);
    }

    [Fact]
    public void ReadSecret_TheDevelopmentSecretInDevelopment_IsAllowed()
    {
        Assert.Equal(CacheSecret.Development, IdempotencySecret.ReadSecret(Configuration(CacheSecret.Development.ToString()), "Development"));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void ReadSecret_ReadsBase64(string environment)
    {
        Assert.Equal(Secret, IdempotencySecret.ReadSecret(Configuration(Secret.ToString()), environment));
    }

    // Neither the message nor an inner exception quotes what was configured.
    [Theory]
    [InlineData("not base64!", "Production")]
    [InlineData("AAAA", "Production")]
    [InlineData("AQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEB", "Development")]
    public void ReadSecret_NotBase64OfThirtyTwoBytes_Throws(string value, string environment)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => IdempotencySecret.ReadSecret(Configuration(value), environment));

        Assert.Contains(IdempotencySecret.SecretKey, exception.Message);
        Assert.DoesNotContain(value, exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ReadSecret_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => IdempotencySecret.ReadSecret(null!, "Production"));
        Assert.Throws<ArgumentNullException>(() => IdempotencySecret.ReadSecret(Configuration(null), null!));
    }

    [Fact]
    public void Value_IsTheSecretGiven()
    {
        Assert.Equal(Secret, new IdempotencySecret(Secret).Value);
    }

    internal static IConfiguration Configuration(string? secret) => new SecretOnly(secret);

    private sealed class SecretOnly(string? secret) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == IdempotencySecret.SecretKey ? secret : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => throw new NotSupportedException();

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
