using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Common.Tests.Exceptions;

public sealed class NotFoundExceptionTests
{
    [Fact]
    public void WithNothingSaid_IsTheStandardDetail()
    {
        Assert.Equal(ErrorDetails.NotFound, new NotFoundException().Message);
    }

    [Fact]
    public void ByTypeAndId_NamesBoth()
    {
        Assert.Equal("No widget with ID '42' exists.", new NotFoundException("widget", "42").Message);
    }

    [Fact]
    public void WithADetail_IsThatDetail()
    {
        Assert.Equal("Gone fishing.", new NotFoundException("Gone fishing.").Message);
    }
}
