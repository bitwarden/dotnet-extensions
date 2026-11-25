using Bitwarden.Server.Sdk.Resources;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the standard error document as the answer to any exception that reaches the pipeline.
/// </summary>
public static class StandardErrorsServiceCollectionExtensions
{
    /// <summary>
    /// Turns any exception that reaches the pipeline into the standard error document: a standard exception into its
    /// own status, anything else into a <c>500</c> that describes nothing, logged against the identifier the response
    /// carries.
    /// </summary>
    /// <remarks>
    /// The pipeline still needs <c>UseExceptionHandler()</c>, and the framework refuses to build that unless problem
    /// details are registered too (<c>AddProblemDetails()</c>); this handler answers first. Pair it with
    /// <see cref="StandardErrorsApplicationBuilderExtensions.UseStandardStatusCodePages"/> for failures that set a
    /// status and throw nothing.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddStandardErrors(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionHandler, ErrorResponseExceptionHandler>());

        return services;
    }
}
