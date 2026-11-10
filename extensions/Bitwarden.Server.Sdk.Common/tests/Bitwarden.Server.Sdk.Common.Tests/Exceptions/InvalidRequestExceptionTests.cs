using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Common.Tests.Exceptions;

public sealed class InvalidRequestExceptionTests
{
    [Fact]
    public void WithNoCode_IsAnInvalidValue()
    {
        var exception = new InvalidRequestException("A name is required.", "/data/attributes/name");

        Assert.Equal("A name is required.", exception.Message);
        Assert.Equal("/data/attributes/name", exception.Pointer);
        Assert.Equal(ErrorCodes.InvalidValue, exception.Code);
    }

    [Fact]
    public void WithACode_KeepsIt()
    {
        var exception = new InvalidRequestException("A name is required.", code: ErrorCodes.FieldRequired);

        Assert.Null(exception.Pointer);
        Assert.Equal(ErrorCodes.FieldRequired, exception.Code);
    }
}
