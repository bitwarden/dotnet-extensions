using System.Net;
using System.Text.Json;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// An operation's own maximum page size, <c>[MaximumPageSize]</c>: bound with the request, the ceiling of the page the
/// handler builds, the bound on the farthest page, and what the OpenAPI document publishes.
/// </summary>
/// <remarks>
/// Compiled twice, as every test here is: with the request delegate generator, and without it
/// (Bitwarden.Server.Sdk.Service.MinimalApi.Tests).
/// </remarks>
public sealed class MaximumPageSizeTests
{
    private const string Path = "/api/v1/probes/large-pages";
    private static readonly Guid Person = new("a1000000-0000-4000-8000-00000000000c");

    [Fact]
    public async Task Get_SizeWithinTheDeclaredMaximum_IsKept()
    {
        using var body = await List($"{Path}?page[size]=1000");

        Assert.Equal(1000, body.RootElement.GetProperty("data").GetArrayLength());
        Assert.Equal(1000, body.RootElement.GetProperty("meta").GetProperty("pageSize").GetInt32());
        Assert.Equal(2, body.RootElement.GetProperty("meta").GetProperty("pageCount").GetInt32());
    }

    [Fact]
    public async Task Get_SizeAboveTheDeclaredMaximum_IsTheMaximum()
    {
        using var body = await List($"{Path}?page[size]=5000&page[number]=2");

        Assert.Equal(200, body.RootElement.GetProperty("data").GetArrayLength());
        Assert.Equal(1000, body.RootElement.GetProperty("meta").GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Get_NoSize_IsTheDefault()
    {
        using var body = await List(Path);

        Assert.Equal(50, body.RootElement.GetProperty("meta").GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Get_UndeclaredOperation_KeepsThePlatformsMaximum()
    {
        using var body = await List("/api/v1/probes?page[size]=5000");

        Assert.Equal(200, body.RootElement.GetProperty("meta").GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Get_PagePastTheFarthestAtTheDeclaredMaximum_Refused()
    {
        await using var service = await SessionHost.StartAsync(productService: true);
        using var request = ProductServiceAttributeTests.Get(
            $"{Path}?page[number]={(int.MaxValue / 1000) + 1}", SessionHost.Token(Person, SessionHost.Scope));

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var error = body.RootElement.GetProperty("errors")[0];
        Assert.Equal("page[number]", error.GetProperty("source").GetProperty("parameter").GetString());
        Assert.Contains($"from 1 to {int.MaxValue / 1000}", error.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenApi_PublishesTheDeclaredMaximum()
    {
        await using var service = await SessionHost.StartAsync(productService: true);

        using var response = await service.Client().GetAsync("/api/openapi.json", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var size = document.RootElement.GetProperty("paths").GetProperty(Path).GetProperty("get").GetProperty("parameters")
            .EnumerateArray().Single(parameter => parameter.GetProperty("name").GetString() == "page[size]");
        Assert.Equal(1000, size.GetProperty("schema").GetProperty("maximum").GetInt32());
        Assert.Equal("How many resources the page holds. Values above 1000 are treated as 1000.", size.GetProperty("description").GetString());
    }

    private static async Task<JsonDocument> List(string path)
    {
        await using var service = await SessionHost.StartAsync(productService: true);
        using var request = ProductServiceAttributeTests.Get(path, SessionHost.Token(Person, SessionHost.Scope));

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
