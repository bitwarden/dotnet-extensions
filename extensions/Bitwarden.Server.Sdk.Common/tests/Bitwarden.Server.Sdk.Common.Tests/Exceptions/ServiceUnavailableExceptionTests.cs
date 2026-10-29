namespace Bitwarden.Server.Sdk.Common.Tests.Exceptions;

// Says which service failed the caller and how, in a sentence the caller can read.
public sealed class ServiceUnavailableExceptionTests
{
    [Fact]
    public void Unreachable_NamesTheServiceAndKeepsTheCause()
    {
        var cause = new HttpRequestException("connection refused");

        var exception = ServiceUnavailableException.Unreachable("billing", cause);

        Assert.Equal("billing", exception.Service);
        Assert.Equal("The billing service could not be reached.", exception.Detail);
        Assert.Equal(exception.Detail, exception.Message);
        Assert.Same(cause, exception.InnerException);
    }

    [Fact]
    public void TimedOut_SaysHowLongInSeconds()
    {
        var exception = ServiceUnavailableException.TimedOut("billing", TimeSpan.FromMilliseconds(2500));

        Assert.Equal("The billing service did not answer within 2.5 seconds.", exception.Detail);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Answered_SaysTheStatus()
    {
        Assert.Equal("The billing service answered 502.", ServiceUnavailableException.Answered("billing", 502).Detail);
    }
}
