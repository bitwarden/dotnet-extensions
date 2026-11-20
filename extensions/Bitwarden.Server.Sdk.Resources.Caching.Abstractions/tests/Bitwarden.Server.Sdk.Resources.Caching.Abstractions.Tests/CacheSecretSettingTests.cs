using Bitwarden.Server.Sdk.CryptoPrimitives;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Bitwarden.Server.Sdk.Resources.Caching.Abstractions.Tests;

/// <summary>
/// The one rule every derived-key secret is read by: required outside Development, never the public development
/// secret where required, and never quoted.
/// </summary>
public sealed class CacheSecretSettingTests
{
    private const string Key = "Example:Secret";

    private static readonly CacheSecret Secret = new(Enumerable.Repeat((byte)1, CacheSecret.Length).ToArray());

    [Theory]
    [InlineData("Development", false)]
    [InlineData("development", false)]
    [InlineData("Production", true)]
    [InlineData("Testing", true)]
    [InlineData("QA", true)]
    public void RequiredIn_EverywhereButDevelopment(string environment, bool expected)
    {
        Assert.Equal(expected, CacheSecretSetting.RequiredIn(environment));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    public void Read_UnsetInDevelopment_IsTheDevelopmentSecret(string environment)
    {
        Assert.Equal(CacheSecret.Development, CacheSecretSetting.Read(Configuration(null), Key, environment));
        Assert.Equal(CacheSecret.Development, CacheSecretSetting.Read(Configuration(" "), Key, environment));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Testing")]
    [InlineData("QA")]
    public void Read_UnsetOutsideDevelopment_Throws(string environment)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CacheSecretSetting.Read(Configuration(null), Key, environment));

        Assert.Equal($"{Key} must be set outside Development.", exception.Message);
    }

    // What else makes it required is named in the refusal.
    [Fact]
    public void Read_Refusals_NameWhatMakesItRequired()
    {
        var unset = Assert.Throws<InvalidOperationException>(() =>
            CacheSecretSetting.Read(Configuration(null), Key, "Production", "when counts are shared in Redis"));
        var development = Assert.Throws<InvalidOperationException>(() =>
            CacheSecretSetting.Read(Configuration(CacheSecret.Development.ToString()), Key, "Production", "when counts are shared in Redis"));

        Assert.Equal($"{Key} must be set when counts are shared in Redis outside Development.", unset.Message);
        Assert.Equal(
            $"{Key} may not be the development secret when counts are shared in Redis outside Development.",
            development.Message);
    }

    [Fact]
    public void Read_TheDevelopmentSecretOutsideDevelopment_Throws()
    {
        var value = CacheSecret.Development.ToString();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CacheSecretSetting.Read(Configuration(value), Key, "Production"));

        Assert.Contains(Key, exception.Message);
        Assert.DoesNotContain(value, exception.Message);
    }

    [Fact]
    public void Read_TheDevelopmentSecretInDevelopment_IsAllowed()
    {
        Assert.Equal(
            CacheSecret.Development,
            CacheSecretSetting.Read(Configuration(CacheSecret.Development.ToString()), Key, "Development"));
    }

    // A caller may narrow when it is required: not required, an unset or development secret is allowed anywhere.
    [Fact]
    public void Read_NotRequired_AllowsUnsetAndTheDevelopmentSecret()
    {
        Assert.Equal(CacheSecret.Development, CacheSecretSetting.Read(null, Key, required: false));
        Assert.Equal(CacheSecret.Development, CacheSecretSetting.Read(CacheSecret.Development.ToString(), Key, required: false));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Read_ReadsBase64(string environment)
    {
        Assert.Equal(Secret, CacheSecretSetting.Read(Configuration(Secret.ToString()), Key, environment));
    }

    // Neither the message nor an inner exception quotes what was configured.
    [Theory]
    [InlineData("not base64!", "Production")]
    [InlineData("AAAA", "Production")]
    [InlineData("AQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEB", "Development")]
    public void Read_NotBase64OfThirtyTwoBytes_Throws(string value, string environment)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CacheSecretSetting.Read(Configuration(value), Key, environment));

        Assert.Equal($"{Key} is not base64 of {CacheSecret.Length} bytes.", exception.Message);
        Assert.DoesNotContain(value, exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Read_NullOrEmptyArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => CacheSecretSetting.Read(null!, Key, "Production"));
        Assert.Throws<ArgumentNullException>(() => CacheSecretSetting.Read(Configuration(null), Key, null!));
        Assert.ThrowsAny<ArgumentException>(() => CacheSecretSetting.Read(Configuration(null), " ", "Production"));
        Assert.ThrowsAny<ArgumentException>(() => CacheSecretSetting.Read(null, "", required: true));
    }

    private static IConfiguration Configuration(string? secret) => new SecretOnly(secret);

    private sealed class SecretOnly(string? secret) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == Key ? secret : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => throw new NotSupportedException();

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
