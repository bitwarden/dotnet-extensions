
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources.Querying;

namespace Bitwarden.Server.Sdk.Resources.Tests.Querying;

/// <summary>
/// One resource's failure in a best-effort bulk operation, described as the error document would
/// describe the same failure alone, and pointed at the item it belongs to.
/// </summary>
public sealed class BulkFailureTests
{
    [Fact]
    public void At_NotFound_IsA404AtThePointer()
    {
        var actual = BulkFailure.At(new NotFoundException("No widget 'a'."), "/ids/3");

        Assert.Equal("404", actual.Status);
        Assert.Equal(ErrorCodes.NotFound, actual.Code);
        Assert.Equal("No widget 'a'.", actual.Detail);
        Assert.Equal("/ids/3", actual.Source?.Pointer);
    }

    [Fact]
    public void At_InvalidRequest_KeepsItsCodeButTakesThePointerGiven()
    {
        var actual = BulkFailure.At(new InvalidRequestException("Too long.", "/data/name", "too-long"), "/data/1");

        Assert.Equal("422", actual.Status);
        Assert.Equal("too-long", actual.Code);
        Assert.Equal("/data/1", actual.Source?.Pointer);
    }

    // Nothing the exception says reaches the caller for a fault of ours.
    [Fact]
    public void At_AnythingElse_IsA500WithoutTheExceptionsMessage()
    {
        var actual = BulkFailure.At(new InvalidOperationException("connection string leaked"), "/ids/0");

        Assert.Equal("500", actual.Status);
        Assert.Equal(ErrorCodes.InternalError, actual.Code);
        Assert.DoesNotContain("leaked", actual.Detail, StringComparison.Ordinal);
        Assert.Null(actual.Id);
    }

    // A service's own refusal reports what it says, as the single-resource request would, rather
    // than falling through to a 500.
    [Fact]
    public void At_ProvidedErrors_ReportsTheFirstWithItsStatusAtThePointer()
    {
        var actual = BulkFailure.At(new Refused(409, "already-answered", "Already answered."), "/ids/2");

        Assert.Equal("409", actual.Status);
        Assert.Equal("already-answered", actual.Code);
        Assert.Equal("Already answered.", actual.Detail);
        Assert.Equal("/ids/2", actual.Source?.Pointer);
    }

    [Fact]
    public void At_Translated_IsReportedAsWhatItMeans()
    {
        var actual = BulkFailure.At(new TimeoutException("lock wait"), "/ids/1", [new ToConflict()]);

        Assert.Equal("409", actual.Status);
        Assert.Equal(ErrorCodes.Conflict, actual.Code);
        Assert.Equal("/ids/1", actual.Source?.Pointer);
    }

    [Fact]
    public void At_TranslatedToProvidedErrors_ReportsWhatTheyProvide()
    {
        var actual = BulkFailure.At(
            new TimeoutException("lock wait"), "/ids/0", [new ToRefusal()]);

        Assert.Equal("422", actual.Status);
        Assert.Equal("refused", actual.Code);
        Assert.Equal("/ids/0", actual.Source?.Pointer);
    }

    [Fact]
    public void At_NoFailure_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BulkFailure.At(null!, "/ids/0"));
    }

    private sealed class Refused(int status, string code, string detail) : Exception(detail), IProvideErrors
    {
        public int StatusCode => status;

        public IReadOnlyList<Error> Errors { get; } =
        [
            new Error { Status = "422", Code = code, Detail = detail, Source = new ErrorSource { Pointer = "/data/name" } },
            new Error { Status = "422", Code = "second", Detail = "Not reported." },
        ];
    }

    private sealed class ToConflict : IExceptionTranslator
    {
        public Exception? Translate(Exception exception) =>
            exception is TimeoutException ? new ConflictException(exception.Message) : null;
    }

    private sealed class ToRefusal : IExceptionTranslator
    {
        public Exception? Translate(Exception exception) =>
            exception is TimeoutException ? new Refused(422, "refused", "Refused.") : null;
    }
}
