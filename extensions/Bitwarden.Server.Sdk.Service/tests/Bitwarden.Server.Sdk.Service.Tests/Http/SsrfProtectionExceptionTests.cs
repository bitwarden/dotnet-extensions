using Bitwarden.Server.Sdk.Service.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

/// <summary>
/// A refusal carries why, and what failed when something did, for whoever catches it.
/// </summary>
public sealed class SsrfProtectionExceptionTests
{
    [Fact]
    public void AMessage_IsKept()
    {
        var exception = new SsrfProtectionException("Refused: the address is internal.");

        Assert.Equal("Refused: the address is internal.", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void WhatFailed_IsKeptAsTheInnerException()
    {
        var failure = new TimeoutException("DNS timed out.");

        var exception = new SsrfProtectionException("Unable to resolve.", failure);

        Assert.Equal("Unable to resolve.", exception.Message);
        Assert.Same(failure, exception.InnerException);
    }

}
