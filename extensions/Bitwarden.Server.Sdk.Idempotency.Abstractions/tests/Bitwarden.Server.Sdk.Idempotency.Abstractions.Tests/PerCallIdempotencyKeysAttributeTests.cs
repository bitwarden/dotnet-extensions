namespace Bitwarden.Server.Sdk.Idempotency.Abstractions.Tests;

public sealed class PerCallIdempotencyKeysAttributeTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_ABlankReason_IsRefused(string reason)
    {
        Assert.Throws<ArgumentException>(() => new PerCallIdempotencyKeysAttribute(reason));
    }

    [Fact]
    public void Reason_IsTheReasonGiven()
    {
        Assert.Equal("A test type.", new PerCallIdempotencyKeysAttribute("A test type.").Reason);
    }

    [Fact]
    public void IsOn_AMarkedType_IsTrue()
    {
        Assert.True(PerCallIdempotencyKeysAttribute.IsOn(typeof(OptedOut)));
    }

    [Fact]
    public void IsOn_AnUnmarkedType_IsFalse()
    {
        Assert.False(PerCallIdempotencyKeysAttribute.IsOn(typeof(PerCallIdempotencyKeysAttributeTests)));
    }

    // Not inherited, so a type derived from an opted-out handler states its own reason or runs inside an operation.
    [Fact]
    public void IsOn_ATypeDerivedFromAMarkedOne_IsFalse()
    {
        Assert.False(PerCallIdempotencyKeysAttribute.IsOn(typeof(DerivedFromOptedOut)));
    }

    [Fact]
    public void IsOn_NoType_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PerCallIdempotencyKeysAttribute.IsOn(null!));
    }

    [PerCallIdempotencyKeys("A test type.")]
    private class OptedOut;

    private sealed class DerivedFromOptedOut : OptedOut;
}
