using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

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
        Converters = { new ResourceDocumentJsonConverterFactory() },
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
    }
}
