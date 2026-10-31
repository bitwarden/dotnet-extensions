using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

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

    [Fact]
    public void At_NoFailure_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BulkFailure.At(null!, "/ids/0"));
    }
}
