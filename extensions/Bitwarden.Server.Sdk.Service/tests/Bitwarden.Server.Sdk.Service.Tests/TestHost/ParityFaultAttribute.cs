using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// Answers one exception in a shape of its own.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ParityFaultAttribute : Attribute, IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (InvalidTimeZoneException fault)
        {
            return Results.Json(new ParityAddress { City = fault.Message }, statusCode: 418);
        }
    }
}
