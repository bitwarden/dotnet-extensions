using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Common.Tests.Exceptions;

public sealed class UnauthenticatedExceptionTests
{
    [Fact]
    public void WithNothingSaid_IsTheStandardDetail()
    {
        Assert.Equal(ErrorDetails.Unauthenticated, new UnauthenticatedException().Message);
    }

    [Fact]
    public void WithADetail_IsThatDetail()
    {
        Assert.Equal("The token expired.", new UnauthenticatedException("The token expired.").Message);
    }
}
