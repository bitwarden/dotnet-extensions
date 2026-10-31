using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A sort refused: a 422 at <c>sort</c> saying what is wrong and offering the fields the caller
/// may use instead, or saying the collection cannot be sorted at all.
/// </summary>
public sealed class InvalidSortExceptionTests
{
    [Fact]
    public void Errors_OfferTheSortableFields()
    {
        var refused = new InvalidSortException("nope", "'nope' is not a field this collection sorts by", ["createdAt", "name"]);

        var actual = Assert.Single(refused.Errors);

        Assert.Equal("422", actual.Status);
        Assert.Equal(ErrorCodes.InvalidValue, actual.Code);
        Assert.Equal(ErrorTitles.InvalidValue, actual.Title);
        Assert.Equal("sort", actual.Source!.Parameter);
        Assert.Equal(
            "'nope' cannot be used to sort: 'nope' is not a field this collection sorts by. Sortable fields are 'createdAt', 'name'.",
            actual.Detail);
        Assert.Equal("'sort' cannot be applied: 'nope' is not a field this collection sorts by.", refused.Message);
    }

    [Fact]
    public void Errors_NothingSortable_SayTheCollectionCannotBeSorted()
    {
        var actual = Assert.Single(new InvalidSortException("name", "nothing is sortable", []).Errors);

        Assert.EndsWith("This collection cannot be sorted.", actual.Detail, StringComparison.Ordinal);
    }
}
