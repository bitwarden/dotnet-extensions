namespace Bitwarden.Server.Sdk.Errors.Tests;

public sealed class ConflictExceptionTests
{
    [Fact]
    public void WithNothingSaid_IsTheStandardDetail()
    {
        Assert.Equal(ErrorDetails.Conflict, new ConflictException().Message);
    }

    [Fact]
    public void WithADetail_IsThatDetail()
    {
        Assert.Equal("The widget is locked.", new ConflictException("The widget is locked.").Message);
    }
}
