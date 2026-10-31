using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// The single-resource document's wire shape, and how a minimal-API handler binds and documents
/// one.
/// </summary>
/// <remarks>
/// The wire shapes here are the public contract: <c>id</c> and <c>type</c> lead, every other member
/// sits under <c>attributes</c>, which is always written, and an unset member (<c>meta</c>
/// included) is absent rather than null.
/// </remarks>
public sealed class ResourceDocumentTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ResourceDocumentJsonConverterFactory() },
    };

    [Fact]
    public void Serialize_ARead_Enveloped()
    {
        var document = new ResourceDocument<Widget>
        {
            Data = new Widget { Id = "w-1", Name = "fake", Size = 3, Version = "7" },
            Meta = new Dictionary<string, object?> { ["note"] = "n" },
        };

        Assert.Equal(
            """{"data":{"id":"w-1","type":"widget","attributes":{"name":"fake","size":3,"version":"7"}},"meta":{"note":"n"}}""",
            JsonSerializer.Serialize(document, Options));
    }

    [Fact]
    public void Serialize_NothingSet_AttributesStillWritten_MetaAbsent()
    {
        var document = new ResourceDocument<Widget> { Data = new Widget() };

        Assert.Equal("""{"data":{"type":"widget","attributes":{}}}""", JsonSerializer.Serialize(document, Options));
    }

    // Fields and Location steer how the document is written; neither is part of it.
    [Fact]
    public void Serialize_FieldsAndLocation_NeverOnTheWire_OtherAttributesLeftOut()
    {
        var document = new ResourceDocument<Widget>
        {
            Data = new Widget { Id = "w-1", Name = "fake", Size = 3 },
            Fields = new HashSet<string> { "size" },
            Location = "/elsewhere/w-1",
        };

        Assert.Equal(
            """{"data":{"id":"w-1","type":"widget","attributes":{"size":3}}}""",
            JsonSerializer.Serialize(document, Options));
    }

    [Fact]
    public void Deserialize_ACreate_FlattenedIntoTheRecord()
    {
        var actual = JsonSerializer.Deserialize<ResourceDocument<Widget>>(
            """{"data":{"type":"widget","attributes":{"name":"fake","size":3}}}""", Options)!;

        Assert.Equal(new Widget { Name = "fake", Size = 3 }, actual.Data);
        Assert.Null(actual.Meta);
    }

    [Fact]
    public void SingleResourceDocument_ReachesTheResourceAndLocation()
    {
        var widget = new Widget { Id = "w-1" };
        ISingleResourceDocument document = new ResourceDocument<Widget> { Data = widget, Location = "/elsewhere/w-1" };

        Assert.Same(widget, document.Resource);
        Assert.Equal("/elsewhere/w-1", document.Location);
    }

    [Fact]
    public void SingleResourceDocument_NoLocation_Null()
    {
        ISingleResourceDocument document = new ResourceDocument<Widget> { Data = new Widget() };

        Assert.Null(document.Location);
    }

    [Fact]
    public async Task BindAsync_ValidBody_TheDocument()
    {
        var context = Request("""{"data":{"type":"widget","attributes":{"name":"  fake  ","size":3}}}""");

        var actual = await ResourceDocument<Widget>.BindAsync(context, null!);

        Assert.NotNull(actual);
        Assert.Equal(new Widget { Name = "fake", Size = 3 }, actual.Data);
        Assert.False(context.Items.ContainsKey(ResourceDocumentReading.FailureKey));
    }

    // The handler never runs for it: the standard filter refuses the request with what is left.
    [Fact]
    public async Task BindAsync_UnusableBody_NothingBound_FailureLeftForTheFilter()
    {
        var context = Request("""{"data":{"type":"gadget","attributes":{}}}""");

        var actual = await ResourceDocument<Widget>.BindAsync(context, null!);

        Assert.Null(actual);
        var failure = Assert.IsType<DocumentFailure>(context.Items[ResourceDocumentReading.FailureKey]);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, failure.StatusCode);
        Assert.Equal("invalid-resource-type", Assert.Single(failure.Errors).Code);
    }

    [Fact]
    public async Task BindAsync_NoContext_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await ResourceDocument<Widget>.BindAsync(null!, null!));
    }

    [Fact]
    public void PopulateMetadata_DocumentsAJsonRequestBody()
    {
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/widgets"), 0);

        ResourceDocument<Widget>.PopulateMetadata(null!, builder);

        var accepts = Assert.IsAssignableFrom<IAcceptsMetadata>(Assert.Single(builder.Metadata));
        Assert.Equal(["application/json"], accepts.ContentTypes);
        Assert.Equal(typeof(ResourceDocument<Widget>), accepts.RequestType);
        Assert.False(accepts.IsOptional);
    }

    [Fact]
    public void PopulateMetadata_NoBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ResourceDocument<Widget>.PopulateMetadata(null!, null!));
    }

    private static DefaultHttpContext Request(string body)
    {
        var services = new ServiceCollection();
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new ResourceDocumentJsonConverterFactory()));

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        return context;
    }

    public sealed record Widget : Resource
    {
        public string? Name { get; init; }

        public int? Size { get; init; }
    }
}
