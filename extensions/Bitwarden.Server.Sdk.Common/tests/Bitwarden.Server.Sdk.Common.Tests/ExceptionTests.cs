using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Common.Tests;

// The exceptions are what a service raises and what its hosting package turns into a response, so
// their message is the detail a caller reads. Each one's default is the standard sentence for it.
public sealed class ExceptionTests
{
    [Fact]
    public void NotFound_WithNothingSaid_IsTheStandardDetail()
    {
        Assert.Equal(ErrorDetails.NotFound, new NotFoundException().Message);
    }

    [Fact]
    public void NotFound_ByTypeAndId_NamesBoth()
    {
        var exception = new NotFoundException("widget", "42");

        Assert.Equal("No widget with ID '42' exists.", exception.Message);
    }

    [Fact]
    public void NotFound_WithADetail_IsThatDetail()
    {
        Assert.Equal("Gone fishing.", new NotFoundException("Gone fishing.").Message);
    }

    [Fact]
    public void Conflict_WithNothingSaid_IsTheStandardDetail()
    {
        Assert.Equal(ErrorDetails.Conflict, new ConflictException().Message);
    }

    [Fact]
    public void Conflict_WithADetail_IsThatDetail()
    {
        Assert.Equal("The widget is locked.", new ConflictException("The widget is locked.").Message);
    }

    [Fact]
    public void Forbidden_WithNothingSaid_IsTheStandardDetail()
    {
        Assert.Equal(ErrorDetails.Forbidden, new ForbiddenException().Message);
    }

    [Fact]
    public void Forbidden_WithADetail_IsThatDetail()
    {
        Assert.Equal("Not yours.", new ForbiddenException("Not yours.").Message);
    }

    [Fact]
    public void Unauthenticated_WithNothingSaid_IsTheStandardDetail()
    {
        Assert.Equal(ErrorDetails.Unauthenticated, new UnauthenticatedException().Message);
    }

    [Fact]
    public void Unauthenticated_WithADetail_IsThatDetail()
    {
        Assert.Equal("The token expired.", new UnauthenticatedException("The token expired.").Message);
    }

    [Fact]
    public void InvalidRequest_WithNoCode_IsAnInvalidValue()
    {
        var exception = new InvalidRequestException("A name is required.", "/data/attributes/name");

        Assert.Equal("A name is required.", exception.Message);
        Assert.Equal("/data/attributes/name", exception.Pointer);
        Assert.Equal(ErrorCodes.InvalidValue, exception.Code);
    }

    [Fact]
    public void InvalidRequest_WithACode_KeepsIt()
    {
        var exception = new InvalidRequestException("A name is required.", code: ErrorCodes.FieldRequired);

        Assert.Null(exception.Pointer);
        Assert.Equal(ErrorCodes.FieldRequired, exception.Code);
    }

    [Fact]
    public void ServiceUnavailable_Unreachable_NamesTheServiceAndKeepsTheCause()
    {
        var cause = new HttpRequestException("connection refused");

        var exception = ServiceUnavailableException.Unreachable("billing", cause);

        Assert.Equal("billing", exception.Service);
        Assert.Equal("The billing service could not be reached.", exception.Detail);
        Assert.Equal(exception.Detail, exception.Message);
        Assert.Same(cause, exception.InnerException);
    }

    [Fact]
    public void ServiceUnavailable_TimedOut_SaysHowLongInSeconds()
    {
        var exception = ServiceUnavailableException.TimedOut("billing", TimeSpan.FromMilliseconds(2500));

        Assert.Equal("The billing service did not answer within 2.5 seconds.", exception.Detail);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ServiceUnavailable_Answered_SaysTheStatus()
    {
        var exception = ServiceUnavailableException.Answered("billing", 502);

        Assert.Equal("The billing service answered 502.", exception.Detail);
    }

    [Fact]
    public void PendingService_NamesWhatItWaitsOnAndIsNotImplemented()
    {
        var exception = new PendingServiceException("mail-provider", "send an email");

        Assert.IsAssignableFrom<NotImplementedException>(exception);
        Assert.Equal("mail-provider", exception.Service);
        Assert.Equal("send an email", exception.Requirement);
        Assert.StartsWith("This needs the mail-provider service to send an email.", exception.Message);
    }

    [Fact]
    public void ProvideErrors_WithNoStatusOfItsOwn_Is422()
    {
        IProvideErrors provider = new SomeErrors();

        Assert.Equal(422, provider.StatusCode);
        Assert.Single(provider.Errors);
    }

    private sealed class SomeErrors : IProvideErrors
    {
        public IReadOnlyList<Error> Errors { get; } = [new Error { Code = ErrorCodes.InvalidValue }];
    }
}
