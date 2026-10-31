using System.Text.Json;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A bulk create's <c>lid</c> for each item, null where it carried none or there is no such item,
/// and the all-or-nothing mode it starts in.
/// </summary>
public sealed class BulkCreateTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ResourceDocumentJsonConverterFactory() },
    };

    [Fact]
    public void LidAt_EachItem_ItsLidOrNull()
    {
        var request = new BulkCreate<Widget> { Data = [new Widget(), new Widget(), new Widget()] };
        request.Lids = ["one", null, "three"];

        Assert.Equal("one", request.LidAt(0));
        Assert.Null(request.LidAt(1));
        Assert.Equal("three", request.LidAt(2));
    }

    // Past the last item, or on a request built in code that was never read, there is no lid.
    [Fact]
    public void LidAt_NoSuchItem_Null()
    {
        var request = new BulkCreate<Widget> { Data = [new Widget()] };

        Assert.Empty(request.Lids);
        Assert.Null(request.LidAt(0));

        request.Lids = ["one"];

        Assert.Null(request.LidAt(7));
    }

    // Until the platform grants another mode, a bulk create is all or nothing.
    [Fact]
    public void Atomicity_NotYetGranted_AllOrNothing()
    {
        Assert.Equal(Atomicity.AllOrNothing, new BulkCreate<Widget> { Data = [] }.Atomicity);
    }

    [Fact]
    public void Read_TwoItemsSharingALid_RefusedWithTheJsonPathToTheSecond()
    {
        var actual = Assert.Throws<ResourceContractException>(() => JsonSerializer.Deserialize<BulkCreate<Widget>>(
            """{ "data": [ { "type": "widget", "lid": "x", "attributes": { "name": "a" } }, { "type": "widget", "lid": "x", "attributes": { "name": "b" } } ] }""",
            Options));

        Assert.Equal("$.data[1].lid", actual.Path);
    }

    public sealed record Widget : Resource
    {
        public string? Name { get; init; }
    }
}
