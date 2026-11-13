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

    // The reason is required on a host's path too, not only when the attribute is built directly.
    [Fact]
    public void IsOn_ATypeMarkedWithABlankReason_Throws()
    {
        Assert.Throws<ArgumentException>(() => PerCallIdempotencyKeysAttribute.IsOn(typeof(OptedOutWithoutAReason)));
    }

    [PerCallIdempotencyKeys("A test type.")]
    private class OptedOut;

    [PerCallIdempotencyKeys(" ")]
    private sealed class OptedOutWithoutAReason;

    private sealed class DerivedFromOptedOut : OptedOut;
}
