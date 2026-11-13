namespace Bitwarden.Server.Sdk.Idempotency.Abstractions.Tests;

public sealed class IdempotencyKeysTests
{
    // The header names reach the wire: a caller and a callee on different versions must still agree on them.
    [Fact]
    public void Headers_AreTheWireNames()
    {
        Assert.Equal("Idempotency-Key", IdempotencyKeys.Header);
        Assert.Equal("Idempotency-Replayed", IdempotencyKeys.ReplayedHeader);
        Assert.Equal(255, IdempotencyKeys.MaximumLength);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("!")]
    [InlineData("~")]
    [InlineData("b9c0297a16c05917f933e9c467632c5ab9174f4335bd4bf16fda9776181b5b3e")]
    public void IsValid_VisibleAscii_IsAccepted(string key)
    {
        Assert.True(IdempotencyKeys.IsValid(key));
    }

    [Fact]
    public void IsValid_TheLongestKey_IsAccepted()
    {
        Assert.True(IdempotencyKeys.IsValid(new string('k', IdempotencyKeys.MaximumLength)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has a space")]
    [InlineData("tab\there")]
    [InlineData("del\u007f")]
    [InlineData("naïve")]
    public void IsValid_NotVisibleAscii_IsRefused(string? key)
    {
        Assert.False(IdempotencyKeys.IsValid(key));
    }

    [Fact]
    public void IsValid_ALongerKey_IsRefused()
    {
        Assert.False(IdempotencyKeys.IsValid(new string('k', IdempotencyKeys.MaximumLength + 1)));
    }
}
