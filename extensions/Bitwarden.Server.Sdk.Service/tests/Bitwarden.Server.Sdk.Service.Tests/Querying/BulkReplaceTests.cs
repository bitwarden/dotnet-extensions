using System.Text.Json;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A bulk replace carries whole resources, each with the id it replaces, read as JSON:API
/// resource objects, and is all-or-nothing unless the operation says otherwise.
/// </summary>
public sealed class BulkReplaceTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ResourceDocumentJsonConverterFactory() },
    };

    [Fact]
    public void Read_ResourceObjects_AreTheResourcesInOrder()
    {
        var actual = JsonSerializer.Deserialize<BulkReplace<Widget>>(
            """
            { "data": [
              { "type": "widget", "id": "2", "attributes": { "name": "b" } },
              { "type": "widget", "id": "1", "attributes": { "name": "a" } }
            ] }
            """,
            Options)!;

        Assert.Equal(["2", "1"], actual.Data.Select(widget => widget.Id));
        Assert.Equal(["b", "a"], actual.Data.Select(widget => widget.Name));
    }

    [Fact]
    public void Atomicity_ByDefault_IsAllOrNothing()
    {
        Assert.Equal(Atomicity.AllOrNothing, new BulkReplace<Widget> { Data = [] }.Atomicity);
    }

    public sealed record Widget : Resource
    {
        public string? Name { get; init; }
    }
}
