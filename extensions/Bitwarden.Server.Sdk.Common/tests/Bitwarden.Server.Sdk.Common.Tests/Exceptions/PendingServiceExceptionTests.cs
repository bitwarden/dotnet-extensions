namespace Bitwarden.Server.Sdk.Common.Tests.Exceptions;

public sealed class PendingServiceExceptionTests
{
    [Fact]
    public void NamesWhatItWaitsOn_AndIsNotImplemented()
    {
        var exception = new PendingServiceException("mail-provider", "send an email");

        Assert.IsAssignableFrom<NotImplementedException>(exception);
        Assert.Equal("mail-provider", exception.Service);
        Assert.Equal("send an email", exception.Requirement);
        Assert.StartsWith("This needs the mail-provider service to send an email.", exception.Message);
    }
}
