using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Common.Tests.Exceptions;

public sealed class ForbiddenExceptionTests
{
    [Fact]
    public void WithNothingSaid_IsTheStandardDetail()
    {
        Assert.Equal(ErrorDetails.Forbidden, new ForbiddenException().Message);
    }

    [Fact]
    public void WithADetail_IsThatDetail()
    {
        Assert.Equal("Not yours.", new ForbiddenException("Not yours.").Message);
    }
}
