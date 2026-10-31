using System.Text.Json;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// Every section of a published contract says what its resources are, and a contract with one
/// that doesn't is not written.
/// </summary>
public sealed class OpenApiExportTests
{
    [Fact]
    public void RequireTagDescriptions_EverySectionDescribed_Passes()
    {
        var document = Document(("Folders", "The folders a person sorts their items into."));

        OpenApiExport.RequireTagDescriptions(document);
    }

    [Fact]
    public void RequireTagDescriptions_SectionWithoutOne_RefusesNamingIt()
    {
        var document = Document(("Folders", "The folders a person sorts their items into."), ("Ciphers", null));

        var actual = Assert.Throws<InvalidOperationException>(() => OpenApiExport.RequireTagDescriptions(document));

        Assert.Contains("Undescribed: Ciphers.", actual.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Folders", actual.Message, StringComparison.Ordinal);
    }

    // The platform's probes are on every service, and no service's endpoints class can describe them.
    [Fact]
    public async Task OpenApi_SystemSection_IsDescribedByThePlatform()
    {
        await using var service = await SessionHost.StartAsync(productService: false);

        using var response = await service.Client().GetAsync("/api/openapi.json", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var system = document.RootElement.GetProperty("tags").EnumerateArray()
            .Single(tag => tag.GetProperty("name").GetString() == "System");
        Assert.False(string.IsNullOrWhiteSpace(system.GetProperty("description").GetString()));
    }

    private static OpenApiDocument Document(params (string Name, string? Description)[] sections)
    {
        var document = new OpenApiDocument { Paths = [], Tags = new HashSet<OpenApiTag>() };

        foreach (var (name, description) in sections)
        {
            document.Tags.Add(new OpenApiTag { Name = name, Description = description });

            var operation = new OpenApiOperation { Tags = new HashSet<OpenApiTagReference> { new(name, document) } };
            document.Paths[$"/api/v1/{name.ToLowerInvariant()}"] = new OpenApiPathItem
            {
                Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Get] = operation },
            };
        }

        return document;
    }
}
