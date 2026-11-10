using System.Reflection;
using System.Text.RegularExpressions;
using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Common.Tests.Errors;

public sealed partial class ErrorCodesTests
{
    private static readonly string[] Codes = typeof(ErrorCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral)
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToArray();

    [Fact]
    public void Codes_AreKebabCase()
    {
        Assert.NotEmpty(Codes);
        Assert.All(Codes, code => Assert.Matches(KebabCase(), code));
    }

    [Fact]
    public void Codes_AreDistinct()
    {
        Assert.Equal(Codes.Length, Codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Codes_KeepTheirValues()
    {
        Assert.Equal("not-found", ErrorCodes.NotFound);
        Assert.Equal("invalid-value", ErrorCodes.InvalidValue);
        Assert.Equal("already-exists", ErrorCodes.AlreadyExists);
        Assert.Equal("key-rotation-in-progress", ErrorCodes.KeyRotationInProgress);
    }

    [GeneratedRegex("^[a-z]+(-[a-z]+)*$")]
    private static partial Regex KebabCase();
}
