using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// Answering an all-or-nothing bulk create: one created resource for each item asked for, the
/// count in <c>meta.affectedCount</c>, and the request's <c>lid</c>s kept to echo.
/// </summary>
public sealed class BulkCreatedDocumentTests
{
    [Fact]
    public void For_OneMadeForEach_CountsThemAndKeepsTheLids()
    {
        var request = new BulkCreate<Widget> { Data = [new Widget(), new Widget()] };
        request.Lids = ["one", null];
        Widget[] created = [new Widget { Id = "1" }, new Widget { Id = "2" }];

        var actual = BulkCreatedDocument<Widget>.For(request, created);

        Assert.Same(created, actual.Data);
        Assert.Equal(2, Assert.Single(actual.Meta!, entry => entry.Key == "affectedCount").Value);
        Assert.Single(actual.Meta!);
        Assert.Equal(["one", null], actual.Lids);
    }

    [Fact]
    public void For_NothingAskedFor_CountsZero()
    {
        var actual = BulkCreatedDocument<Widget>.For(new BulkCreate<Widget> { Data = [] }, []);

        Assert.Equal(0, actual.Meta!["affectedCount"]);
    }

    // Anything but one for one is a bug in the operation, caught before it reaches a caller.
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void For_NotOneMadeForEach_Throws(int made)
    {
        var request = new BulkCreate<Widget> { Data = [new Widget(), new Widget()] };

        var actual = Assert.Throws<ArgumentException>(() => BulkCreatedDocument<Widget>.For(
            request, [.. Enumerable.Range(0, made).Select(i => new Widget { Id = $"{i}" })]));

        Assert.Equal("created", actual.ParamName);
        Assert.StartsWith($"2 resources were asked for and {made} were made.", actual.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void For_NoRequest_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BulkCreatedDocument<Widget>.For(null!, []));
    }

    [Fact]
    public void For_NothingCreated_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BulkCreatedDocument<Widget>.For(new BulkCreate<Widget> { Data = [] }, null!));
    }

    public sealed record Widget : Resource;
}
