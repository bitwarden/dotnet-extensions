using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A query parameter refused: a 422 at the parameter, by default because it is not a whole number
/// of 1 or more, quoting the value only in the error the caller reads.
/// </summary>
public sealed class InvalidQueryExceptionTests
{
    [Fact]
    public void Errors_ByDefault_AskForAWholeNumber()
    {
        var refused = new InvalidQueryException("page[size]", "-1");

        var actual = Assert.Single(refused.Errors);

        Assert.Equal("422", actual.Status);
        Assert.Equal(ErrorCodes.InvalidValue, actual.Code);
        Assert.Equal(ErrorTitles.InvalidValue, actual.Title);
        Assert.Equal("page[size]", actual.Source!.Parameter);
        Assert.Equal("The 'page[size]' parameter must be a whole number of 1 or more, not '-1'.", actual.Detail);
        Assert.Equal("The 'page[size]' parameter must be a whole number of 1 or more.", refused.Message);
    }

    [Fact]
    public void Errors_WithAnExpectation_SayIt()
    {
        var actual = Assert.Single(new InvalidQueryException("page[number]", "9", "must be 5 or less").Errors);

        Assert.Equal("The 'page[number]' parameter must be 5 or less, not '9'.", actual.Detail);
    }
}
