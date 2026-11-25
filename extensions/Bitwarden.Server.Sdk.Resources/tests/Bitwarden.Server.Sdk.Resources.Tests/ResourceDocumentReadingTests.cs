using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Resources.Tests;

/// <summary>
/// Reading a request body into a document: validated in full first, the document's read-only
/// members dropped by wire name, and a failure left in the request's items for the standard filter.
/// </summary>
public sealed class ResourceDocumentReadingTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new ResourceDocumentJsonConverterFactory(), new JsonStringEnumConverter(EnumValues.Naming) },
    };

    [Fact]
    public async Task ReadAsync_ValidBody_TheDocumentAndNoFailure()
    {
        var http = Request("""{"data":{"type":"widget","attributes":{"name":"fake"}}}""");

        var (document, failure) = await ResourceDocumentReading.ReadAsync(http, typeof(ResourceDocument<Widget>), typeof(Widget), Options);

        Assert.Null(failure);
        Assert.Equal(new Widget { Name = "fake" }, Assert.IsType<ResourceDocument<Widget>>(document).Data);
        Assert.Empty(http.Items);
    }

    // meta is the server's to write; a caller sending it is not refused, and it is not read.
    [Fact]
    public async Task ReadAsync_ReadOnlyDocumentMemberSent_Dropped()
    {
        var http = Request("""{"data":{"type":"widget","attributes":{"name":"fake"}},"meta":{"note":"n"}}""");

        var (document, _) = await ResourceDocumentReading.ReadAsync(http, typeof(ResourceDocument<Widget>), typeof(Widget), Options);

        Assert.Null(Assert.IsType<ResourceDocument<Widget>>(document).Meta);
    }

    [Fact]
    public async Task ReadAsync_UnusableBody_FailureReturnedAndLeftInTheItems()
    {
        var http = Request("");

        var (document, failure) = await ResourceDocumentReading.ReadAsync(http, typeof(ResourceDocument<Widget>), typeof(Widget), Options);

        Assert.Null(document);
        Assert.NotNull(failure);
        Assert.Equal(StatusCodes.Status400BadRequest, failure.StatusCode);
        Assert.Same(failure, http.Items[ResourceDocumentReading.FailureKey]);
    }

    // Only id and type come from beside attributes. The options read members ignoring case, as a
    // host's do, so the member's casing must not matter; nor its value, which is never read.
    [Theory]
    [InlineData("\"createdBy\":\"x\"")]
    [InlineData("\"CreatedBy\":\"x\"")]
    [InlineData("\"createdAt\":\"not a date\"")]
    [InlineData("\"name\":\"outside\"")]
    public async Task ReadAsync_AMemberBesideAttributes_NotBound(string member)
    {
        var http = Request($$"""{"data":{"type":"widget",{{member}},"attributes":{"name":"fake"} } }""");

        var (document, failure) = await ResourceDocumentReading.ReadAsync(http, typeof(ResourceDocument<Widget>), typeof(Widget), Options);

        Assert.Null(failure);
        Assert.Equal(new Widget { Name = "fake" }, Assert.IsType<ResourceDocument<Widget>>(document).Data);
    }

    // Each value is the JSON kind its member takes, so binding passes it, but not one its type can
    // hold. The caller's mistake, refused at the value; the detail does not repeat what was sent.
    [Theory]
    [InlineData("\"key\":\"abc\"", "/data/attributes/key", "key")]
    [InlineData("\"when\":\"2024-13-45\"", "/data/attributes/when", "when")]
    [InlineData("\"count\":1e20", "/data/attributes/count", "count")]
    [InlineData("\"colour\":\"dark_blue\"", "/data/attributes/colour", "colour")]
    [InlineData("\"part\":{\"size\":\"abc\"}", "/data/attributes/part/size", "size")]
    [InlineData("\"parts\":[{\"size\":1},{\"size\":\"abc\"}]", "/data/attributes/parts/1/size", "size")]
    public async Task ReadAsync_AValueItsTypeCannotHold_InvalidValueAtIt(string member, string pointer, string field)
    {
        var http = Request($$"""{"data":{"type":"widget","attributes":{"name":"fake",{{member}} } } }""");

        var (document, failure) = await ResourceDocumentReading.ReadAsync(http, typeof(ResourceDocument<Widget>), typeof(Widget), Options);

        Assert.Null(document);
        Assert.NotNull(failure);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, failure.StatusCode);
        var error = Assert.Single(failure.Errors);
        Assert.Equal(Bitwarden.Server.Sdk.Errors.ErrorCodes.InvalidValue, error.Code);
        Assert.Equal(Bitwarden.Server.Sdk.Errors.ErrorTitles.InvalidValue, error.Title);
        Assert.Equal($"The '{field}' field has an invalid value.", error.Detail);
        Assert.Equal(pointer, error.Source?.Pointer);
        Assert.Same(failure, http.Items[ResourceDocumentReading.FailureKey]);
    }

    [Fact]
    public void FailureKey_IsStable()
    {
        Assert.Equal("bitwarden.document-failure", ResourceDocumentReading.FailureKey);
    }

    private static DefaultHttpContext Request(string body)
    {
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        return http;
    }

    public sealed record Widget : Resource
    {
        public string? Name { get; init; }

        public Guid? Key { get; init; }

        public DateTimeOffset? When { get; init; }

        public int? Count { get; init; }

        public Shade? Colour { get; init; }

        public Part? Part { get; init; }

        public List<Part>? Parts { get; init; }
    }

    public enum Shade
    {
        Red,
        DarkBlue,
    }

    public sealed record Part
    {
        public int? Size { get; init; }
    }
}
