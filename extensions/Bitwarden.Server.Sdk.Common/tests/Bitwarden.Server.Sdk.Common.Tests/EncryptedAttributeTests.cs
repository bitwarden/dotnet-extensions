using System.ComponentModel.DataAnnotations;

namespace Bitwarden.Server.Sdk.Common.Tests;

/// <summary>
/// <see cref="EncryptedAttribute"/> holds a value to its length bound, and, when <c>WellFormed</c> is set, to an
/// encrypted string's shape (see <see cref="EncryptedStringFormatTests"/>), refusing a malformed value in the
/// words the Bitwarden server uses.
/// </summary>
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

    [Theory]
    [InlineData("2.AAAAAAAAAAAAAAAAAAAAAA==|AAAA|AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", true)]
    [InlineData("not encrypted", false)]
    [InlineData("2.project|name|mac", false)]
    public void IsValid_WellFormed_HoldsTheValueToAnEncryptedStringsShape(string value, bool valid)
    {
        // Placeholders, made up for the test, that protect nothing.
        var attribute = new EncryptedAttribute { MaxLength = 1000, WellFormed = true };

        Assert.Equal(valid, attribute.IsValid(value));
    }

    [Fact]
    public void IsValid_NotWellFormedByDefault_ChecksOnlyTheLength()
    {
        var attribute = new EncryptedAttribute { MaxLength = 100 };

        Assert.True(attribute.IsValid("not encrypted"));
    }

    [Fact]
    public void GetValidationResult_Malformed_IsRefusedInTheServersWords()
    {
        var attribute = new EncryptedAttribute { MaxLength = 1000, WellFormed = true };
        var context = new ValidationContext(new object()) { DisplayName = "Value", MemberName = "Value" };

        var result = attribute.GetValidationResult("not encrypted", context);

        Assert.Equal("Value is not a valid encrypted string.", result?.ErrorMessage);
        Assert.Equal(["Value"], result?.MemberNames);
    }

    [Fact]
    public void GetValidationResult_WellFormedButTooLong_IsRefusedForItsLength()
    {
        var attribute = new EncryptedAttribute { MaxLength = 10, WellFormed = true };
        var context = new ValidationContext(new object()) { DisplayName = "Value", MemberName = "Value" };

        var result = attribute.GetValidationResult(
            "2.AAAAAAAAAAAAAAAAAAAAAA==|AAAA|AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", context);

        Assert.Equal("The field Value must be an encrypted string of at most 10 characters.", result?.ErrorMessage);
    }

    [Fact]
    public void GetValidationResult_Valid_IsSuccess()
    {
        var attribute = new EncryptedAttribute { MaxLength = 1000, WellFormed = true };
        var context = new ValidationContext(new object()) { DisplayName = "Value", MemberName = "Value" };

        var result = attribute.GetValidationResult(
            "2.AAAAAAAAAAAAAAAAAAAAAA==|AAAA|AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", context);

        Assert.Equal(ValidationResult.Success, result);
    }
}
