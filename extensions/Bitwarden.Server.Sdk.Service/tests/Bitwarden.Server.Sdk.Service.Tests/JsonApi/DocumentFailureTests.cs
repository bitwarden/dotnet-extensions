using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// A failed body: the status that describes it and every error found, as the binding returns it.
/// </summary>
public sealed class DocumentFailureTests
{
    [Fact]
    public void New_KeepsTheStatusAndErrors()
    {
        Error[] errors = [new Error { Code = "field-required" }];

        var actual = new DocumentFailure(422, errors);

        Assert.Equal(422, actual.StatusCode);
        Assert.Same(errors, actual.Errors);
        Assert.Equal(new DocumentFailure(422, errors), actual);
    }

    // The binding's two outcomes: unparseable is 400, parseable but wrong is 422.
    [Theory]
    [InlineData("{", 400)]
    [InlineData("""{ "data": null }""", 422)]
    public void TryBind_Failure_StatusSaysWhichLevelFailed(string body, int status)
    {
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };

        var actual = ResourceDocumentBinding.TryBind(body, options.GetTypeInfo(typeof(Probe)), "probe", new HashSet<string>(), out _);

        Assert.Equal(status, actual!.StatusCode);
    }

    public sealed record Probe : Resource;
}
