namespace Bitwarden.Server.Sdk.Common.Tests;

// A member holding ciphertext is checked for its length and nothing else, and a declaration that
// states no bound is a mistake in the resource, refused rather than accepting every value.
public sealed class EncryptedAttributeTests
{
    [Fact]
    public void IsValid_Absent_IsLeftToNullability()
    {
        Assert.True(new EncryptedAttribute { MaxLength = 10 }.IsValid(null));
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void IsValid_AString_IsHeldToTheBound(int length, bool valid)
    {
        var attribute = new EncryptedAttribute { MaxLength = 10 };

        Assert.Equal(valid, attribute.IsValid(new string('a', length)));
    }

    [Fact]
    public void IsValid_NotAString_IsRefused()
    {
        Assert.False(new EncryptedAttribute { MaxLength = 10 }.IsValid(42));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void IsValid_WithNoPositiveBound_Throws(int maxLength)
    {
        var attribute = new EncryptedAttribute { MaxLength = maxLength };

        Assert.Throws<InvalidOperationException>(() => attribute.IsValid("a"));
        Assert.Throws<InvalidOperationException>(attribute.EnsureBounded);
    }

    [Fact]
    public void FormatErrorMessage_NamesTheFieldAndTheBound()
    {
        var attribute = new EncryptedAttribute { MaxLength = 10 };

        Assert.Equal(
            "The field notes must be an encrypted string of at most 10 characters.",
            attribute.FormatErrorMessage("notes"));
    }
}
