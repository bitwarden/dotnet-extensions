using Bitwarden.Server.Sdk.Idempotency;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// The contract marks each operation that accepts idempotency keys, and only those, for the client packages built
/// from it.
/// </summary>
public sealed class IdempotencyKeyOperationTransformerTests
{
    [Fact]
    public async Task Document_MarksTheOperationsThatAcceptKeys_AndNoOthers()
    {
        var document = await GenerateAsync();

        var create = document.Paths["/api/v1/probes"]!.Operations![HttpMethod.Post];
        var replace = document.Paths["/api/v1/probes/{id}"]!.Operations![HttpMethod.Put];

        var marked = Assert.IsType<JsonNodeExtension>(create.Extensions![IdempotencyKeys.Extension]);
        Assert.True(marked.Node.GetValue<bool>());
        Assert.False(replace.Extensions?.ContainsKey(IdempotencyKeys.Extension) ?? false);
    }

    private static async Task<OpenApiDocument> GenerateAsync()
    {
        await using var app = new Probe().Build();
        await app.StartAsync(TestContext.Current.CancellationToken);

        return await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);
    }

    private sealed class Probe() : Service("idempotency-probe", "Idempotency Probe", [])
    {
        protected override WebApplicationBuilder CreateBuilder()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            return builder;
        }

        protected override void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("api/v1/probes", Create);
            endpoints.MapPut("api/v1/probes/{id}", Replace);
        }

        [AcceptsIdempotencyKey]
        private static IResult Create() => TypedResults.Created();

        private static IResult Replace(string id) => TypedResults.NoContent();
    }
}
