using Bitwarden.Server.Sdk.Service.Endpoints;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// A section is published under its handler class's name split into words, or the name its
/// <see cref="ApiTagAttribute"/> gives, with the description written once for it; sections no
/// handler class owns are kept, and the platform's own is described.
/// </summary>
public sealed class TagDocumentTransformerTests
{
    /// <summary>
    /// A handler class named for its resource.
    /// </summary>
    public static class ProviderUsersEndpoints
    {
        /// <summary>
        /// A handler.
        /// </summary>
        public static void ListProviderUsers()
        {
        }
    }

    /// <summary>

    /// A handler class whose name begins with a run of capitals.

    /// </summary>
    public static class SsoConfigsEndpoints
    {
        /// <summary>
        /// A handler.
        /// </summary>
        public static void ListSsoConfigs()
        {
        }
    }

    /// <summary>

    /// A handler class owning a section and describing it.

    /// </summary>
    [ApiTag("Passkeys", Description = "The passkeys a person signs in with.")]
    public static class PasskeysEndpoints
    {
        /// <summary>
        /// A handler.
        /// </summary>
        public static void ListPasskeys()
        {
        }
    }

    /// <summary>

    /// A handler class publishing into another's section without describing it.

    /// </summary>
    [ApiTag("Passkeys")]
    public static class PasskeyOptionsEndpoints
    {
        /// <summary>
        /// A handler.
        /// </summary>
        public static void GetPasskeyOptions()
        {
        }
    }

    [Theory]
    [InlineData(typeof(ProviderUsersEndpoints), "ProviderUsers", "Provider Users")]
    [InlineData(typeof(SsoConfigsEndpoints), "SsoConfigs", "Sso Configs")]
    public async Task TransformAsync_HandlerClass_PublishesItsNameAsWords(Type handlers, string tag, string expected)
    {
        var (document, operation) = Document(tag);

        await Transform(document, Description(handlers, tag));

        Assert.Equal([expected], operation.Tags!.Select(each => each.Name));
        Assert.Equal([expected], document.Tags!.Select(each => each.Name));
    }

    [Fact]
    public async Task TransformAsync_ApiTag_NamesAndDescribesTheSection()
    {
        var (document, operation) = Document("Passkeys");

        await Transform(document, Description(typeof(PasskeysEndpoints), "Passkeys"));

        Assert.Equal(["Passkeys"], operation.Tags!.Select(each => each.Name));
        var tag = Assert.Single(document.Tags!);
        Assert.Equal("The passkeys a person signs in with.", tag.Description);
    }

    // Whichever class is described last, the section keeps the one description written for it.
    [Fact]
    public async Task TransformAsync_SharedSection_TakesTheDescriptionWrittenOnce()
    {
        var (document, options) = Document("PasskeyOptions");
        var passkeys = Operation(document, "/api/v1/passkeys", "Passkeys");

        await Transform(
            document,
            Description(typeof(PasskeyOptionsEndpoints), "PasskeyOptions"),
            Description(typeof(PasskeysEndpoints), "Passkeys"));

        Assert.Equal(["Passkeys"], options.Tags!.Select(each => each.Name));
        Assert.Equal(["Passkeys"], passkeys.Tags!.Select(each => each.Name));
        var tag = Assert.Single(document.Tags!);
        Assert.Equal("The passkeys a person signs in with.", tag.Description);
    }

    [Fact]
    public async Task TransformAsync_PlatformSection_IsKeptAndDescribed()
    {
        var (document, _) = Document("ProviderUsers");
        var health = Operation(document, "/alive", SystemEndpoints.Tag);
        document.Tags!.Add(new OpenApiTag { Name = SystemEndpoints.Tag });

        await Transform(document, Description(typeof(ProviderUsersEndpoints), "ProviderUsers"));

        Assert.Equal([SystemEndpoints.Tag], health.Tags!.Select(each => each.Name));
        var system = Assert.Single(document.Tags!, tag => tag.Name == SystemEndpoints.Tag);
        Assert.Equal(SystemEndpoints.TagDescription, system.Description);
    }

    [Fact]
    public async Task TransformAsync_OtherSectionNoHandlerClassOwns_IsKeptAsItIs()
    {
        var (document, _) = Document("ProviderUsers");
        document.Tags!.Add(new OpenApiTag { Name = "Legacy" });

        await Transform(document, Description(typeof(ProviderUsersEndpoints), "ProviderUsers"));

        Assert.Equal(["Legacy", "Provider Users"], document.Tags!.Select(tag => tag.Name!).Order(StringComparer.Ordinal));
        Assert.Null(document.Tags!.Single(tag => tag.Name == "Legacy").Description);
    }

    [Fact]
    public async Task TransformAsync_OperationWithoutTags_IsLeftAlone()
    {
        var (document, operation) = Document("ProviderUsers");
        operation.Tags!.Clear();

        await Transform(document, Description(typeof(ProviderUsersEndpoints), "ProviderUsers"));

        Assert.Empty(operation.Tags);
    }

    [Fact]
    public async Task TransformAsync_NotAStandardEndpoint_IsNotRenamed()
    {
        var (document, operation) = Document("ProviderUsers");
        var description = Description(typeof(ProviderUsersEndpoints), "ProviderUsers");
        description.ActionDescriptor.EndpointMetadata.Remove(StandardEndpointMetadata.Instance);

        await Transform(document, description);

        Assert.Equal(["ProviderUsers"], operation.Tags!.Select(each => each.Name));
        Assert.Equal(["ProviderUsers"], document.Tags!.Select(each => each.Name));
    }

    private static (OpenApiDocument Document, OpenApiOperation Operation) Document(string tag)
    {
        var document = new OpenApiDocument { Paths = [], Tags = new HashSet<OpenApiTag> { new() { Name = tag } } };
        return (document, Operation(document, "/api/v1/" + tag.ToLowerInvariant(), tag));
    }

    private static OpenApiOperation Operation(OpenApiDocument document, string path, string tag)
    {
        var operation = new OpenApiOperation { Tags = new HashSet<OpenApiTagReference> { new(tag, document) } };
        document.Paths[path] = new OpenApiPathItem
        {
            Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Get] = operation },
        };
        return operation;
    }

    // As StandardEndpointApiDescriptionProvider leaves it: the handler in the metadata, and the
    // class it is declared on as the "controller".
    private static ApiDescription Description(Type handlers, string controller)
    {
        var action = new ActionDescriptor
        {
            EndpointMetadata = [StandardEndpointMetadata.Instance, handlers.GetMethods().First(method => method.DeclaringType == handlers)],
        };
        action.RouteValues["controller"] = controller;

        return new ApiDescription { ActionDescriptor = action, HttpMethod = "GET", RelativePath = "api/v1/" + controller.ToLowerInvariant() };
    }

    private static Task Transform(OpenApiDocument document, params ApiDescription[] descriptions) =>
        new TagDocumentTransformer().TransformAsync(document, new OpenApiDocumentTransformerContext
        {
            DocumentName = "v1",
            DescriptionGroups = [new ApiDescriptionGroup("v1", descriptions)],
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);
}
