using Bitwarden.Server.Sdk.Sagas;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Sagas;

/// <summary>
/// Counts each endpoint's writes (<see cref="ServiceWrites"/>), so one whose writes span services without saying how it
/// handles a failure part way (<see cref="SpansServicesAttribute"/> on its handler method) is noticed.
/// </summary>
/// <remarks>
/// Last in the platform's pipeline, once the operation is chosen and a repeated write has been answered from its
/// record. A refusal the operation caught and went on past is thrown again when it returns, so it can't succeed as if
/// the refused write had been made.
/// </remarks>
/// <param name="next">The rest of the pipeline.</param>
internal sealed class OperationWrites(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http)
    {
        if (http.GetEndpoint() is not { } endpoint)
        {
            await next(http);
            return;
        }

        using var operation = ServiceWrites.Begin(
            endpoint.DisplayName ?? http.Request.Method,
            endpoint.Metadata.GetMetadata<SpanDeclarationAttribute>(),
            http.RequestServices.GetService<IServiceWritesGuard>());

        await next(http);

        operation.ThrowIfRefused();
    }
}
