using System.Text.Json;
using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Common.Tests.Errors;

// The error document is shared by the service writing it and the client reading it, so it must
// survive a round trip through JSON as the web defaults write it.
public sealed class ErrorResponseTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void RoundTrip_KeepsEveryMember()
    {
        var response = new ErrorResponse
        {
            Errors =
            [
                new Error
                {
                    Id = "e1",
                    Status = "422",
                    Code = ErrorCodes.FieldRequired,
                    Title = ErrorTitles.FieldRequired,
                    Detail = ErrorDetails.FieldRequired("name"),
                    Source = new ErrorSource { Pointer = "/data/attributes/name", Parameter = "q", Header = "If-Match" },
                },
            ],
        };

        var read = JsonSerializer.Deserialize<ErrorResponse>(JsonSerializer.Serialize(response, Web), Web)!;

        var error = Assert.Single(read.Errors);
        Assert.Equal(response.Errors[0] with { Source = null }, error with { Source = null });
        Assert.Equal(response.Errors[0].Source, error.Source);
    }

    [Fact]
    public void Serialize_UsesCamelCaseNames()
    {
        var json = JsonSerializer.Serialize(
            new ErrorResponse { Errors = [new Error { Code = ErrorCodes.NotFound, Source = new ErrorSource { Pointer = "/id" } }] },
            Web);

        using var document = JsonDocument.Parse(json);
        var error = document.RootElement.GetProperty("errors")[0];
        Assert.Equal(ErrorCodes.NotFound, error.GetProperty("code").GetString());
        Assert.Equal("/id", error.GetProperty("source").GetProperty("pointer").GetString());
    }

    [Fact]
    public void FailedResource_IsEqualByTypeAndId()
    {
        Assert.Equal(new FailedResource("widget", "1"), new FailedResource("widget", "1"));
        Assert.NotEqual(new FailedResource("widget", "1"), new FailedResource("widget", "2"));
    }
}
