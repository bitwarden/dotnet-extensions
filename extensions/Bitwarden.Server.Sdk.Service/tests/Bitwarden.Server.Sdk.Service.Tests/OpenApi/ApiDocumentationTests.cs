using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// The document is OpenAPI 3.0, titled for the service, bound to no server, and offers exactly the
/// credentials the service accepts: its own credential only where Identity is known, the session
/// on a product service, and the caller-context headers on any other.
/// </summary>
public sealed class ApiDocumentationTests
{
    private const string Identity = "https://identity.example.test";

    [Fact]
    public void Configure_WritesOpenApi30WithTheDomainsSchemaNames()
    {
        var options = new OpenApiOptions();

        ApiDocumentation.Configure(options, "Widget Service", tokenUrl: null);

        Assert.Equal(OpenApiSpecVersion.OpenApi3_0, options.OpenApiVersion);
        Assert.Equal(ApiDocumentation.SpecVersion, options.OpenApiVersion);
        Assert.Equal(SchemaNames.For, options.CreateSchemaReferenceId);
    }

    [Fact]
    public async Task Document_IsTitledForTheServiceWithNoServers()
    {
        var document = await GenerateAsync(new Domain(identity: null));

        Assert.Equal("Documentation Probe", document.Info.Title);
        Assert.Empty(document.Servers ?? []);
    }

    [Fact]
    public async Task Document_DomainService_OffersTheCallerContextHeaders()
    {
        var document = await GenerateAsync(new Domain(identity: null));

        var schemes = document.Components!.SecuritySchemes!;
        Assert.Equal(["context", "organization", "provider", "user"], schemes.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(BitwardenHeaders.Organization, schemes["organization"].Name);
        Assert.Equal(BitwardenHeaders.User, schemes["user"].Name);
        Assert.Equal(BitwardenHeaders.Provider, schemes["provider"].Name);
        Assert.Equal(BitwardenHeaders.Context, schemes["context"].Name);
        Assert.All(schemes.Values, scheme =>
        {
            Assert.Equal(SecuritySchemeType.ApiKey, scheme.Type);
            Assert.Equal(ParameterLocation.Header, scheme.In);
        });
    }

    [Fact]
    public async Task Document_IdentityKnown_OffersTheServicesOwnCredential()
    {
        var document = await GenerateAsync(new Domain(Identity));

        var service = document.Components!.SecuritySchemes!["service"];
        Assert.Equal(SecuritySchemeType.OAuth2, service.Type);
        Assert.Equal(new Uri($"{Identity}/connect/token"), service.Flows!.ClientCredentials!.TokenUrl);
    }

    [Fact]
    public async Task Document_IdentityUnknown_OmitsTheServicesOwnCredential()
    {
        var document = await GenerateAsync(new Domain(identity: null));

        Assert.False(document.Components!.SecuritySchemes!.ContainsKey("service"));
    }

    [Fact]
    public async Task Document_ProductService_OffersTheSessionAlone()
    {
        var document = await GenerateAsync(new Product());

        var schemes = document.Components!.SecuritySchemes!;
        var session = Assert.Single(schemes).Value;
        Assert.Equal("session", schemes.Keys.Single());
        Assert.Equal(SecuritySchemeType.Http, session.Type);
        Assert.Equal("bearer", session.Scheme);
        Assert.Equal("JWT", session.BearerFormat);
    }

    // Declared document-wide, so every operation inherits every credential offered.
    [Fact]
    public async Task Document_RequiresEveryOfferedCredential()
    {
        var document = await GenerateAsync(new Domain(Identity));

        var requirement = Assert.Single(document.Security!);
        Assert.Equal(
            ["context", "organization", "provider", "service", "user"],
            requirement.Keys.Select(scheme => scheme.Reference.Id!).Order(StringComparer.Ordinal));
        Assert.All(requirement.Values, Assert.Empty);
    }

    private static async Task<OpenApiDocument> GenerateAsync(Service service)
    {
        await using var app = service.Build();
        await app.StartAsync(TestContext.Current.CancellationToken);

        return await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);
    }

    private static WebApplicationBuilder Builder(string? identity)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();

        if (identity is not null)
        {
            builder.Configuration["globalSettings:baseServiceUri:identity"] = identity;
        }

        return builder;
    }

    private sealed class Domain(string? identity) : Service("documentation-probe", "Documentation Probe", [])
    {
        protected override WebApplicationBuilder CreateBuilder() => Builder(identity);
    }

    [ProductService]
    private sealed class Product() : Service("documentation-probe", "Documentation Probe", [])
    {
        protected override WebApplicationBuilder CreateBuilder() => Builder(identity: null);
    }
}
