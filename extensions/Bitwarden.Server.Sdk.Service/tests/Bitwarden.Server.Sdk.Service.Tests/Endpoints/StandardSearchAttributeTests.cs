namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A search is a <c>POST</c> to <c>{collection}:search</c> that succeeds with <c>200 OK</c> and is
/// a read in every other respect, so it takes the read scope.
/// </summary>
public sealed class StandardSearchAttributeTests
{
    [Fact]
    public void Collection_Given_PostToItsSearchSuffix()
    {
        var actual = new StandardSearchAttribute("api/v1/events");

        Assert.Equal(["POST"], actual.HttpMethods);
        Assert.Equal("~/api/v1/events:search", actual.Template);
        Assert.Equal(200, actual.SuccessStatusCode);
        Assert.True(((IStandardOperation)actual).IsRead);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Collection_Blank_Refused(string collection)
    {
        Assert.ThrowsAny<ArgumentException>(() => new StandardSearchAttribute(collection));
    }
}
