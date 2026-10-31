using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A filter value refused: a 422 at the filter's parameter, either saying what was expected and
/// what came instead, or in the words of whoever refused it.
/// </summary>
public sealed class InvalidFilterExceptionTests
{
    [Fact]
    public void Errors_FromAnExpectation_SayWhatWasExpectedAndWhatCame()
    {
        var refused = new InvalidFilterException("filter[id]", "x", "expects a number");

        var actual = Assert.Single(refused.Errors);

        Assert.Equal("422", actual.Status);
        Assert.Equal(ErrorCodes.InvalidValue, actual.Code);
        Assert.Equal(ErrorTitles.InvalidValue, actual.Title);
        Assert.Equal("filter[id]", actual.Source!.Parameter);
        Assert.Equal("'filter[id]' expects a number, not 'x'.", actual.Detail);
        Assert.Equal(actual.Detail, refused.Message);
    }

    [Fact]
    public void Because_TakesTheDetailAsWritten()
    {
        var refused = InvalidFilterException.Because("filter[at]", "No range ends before it starts.");

        var actual = Assert.Single(refused.Errors);

        Assert.Equal("No range ends before it starts.", actual.Detail);
        Assert.Equal("filter[at]", actual.Source!.Parameter);
    }
}
