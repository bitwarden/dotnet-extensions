using System.Text.Json;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// The exception carries the offending field as a JSON Pointer and as the JSON path the
/// serializer reports, so the error names the field either way it reaches model state.
/// </summary>
public sealed class ResourceContractExceptionTests
{
    [Theory]
    [InlineData("/data/1/lid", "$.data[1].lid")]
    [InlineData("/data/type", "$.data.type")]
    [InlineData("/data/attributes/name", "$.data.attributes.name")]
    [InlineData("/", "$")]
    public void Path_IsThePointerAsAJsonPath(string pointer, string path)
    {
        var actual = new ResourceContractException("Wrong.", pointer);

        Assert.Equal(path, actual.Path);
        Assert.Equal(pointer, actual.Pointer);
    }

    [Fact]
    public void New_KeepsTheMessage_AndIsAJsonException()
    {
        JsonException actual = new ResourceContractException("A resource must declare \"type\".", "/data/type");

        Assert.Equal("A resource must declare \"type\".", actual.Message);
        Assert.Null(actual.InnerException);
        Assert.Null(actual.LineNumber);
    }
}
