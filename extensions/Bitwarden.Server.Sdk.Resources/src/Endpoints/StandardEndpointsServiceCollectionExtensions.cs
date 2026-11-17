using Bitwarden.Server.Sdk.Resources.Endpoints;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers what standard endpoints need while they are built and described.
/// </summary>
public static class StandardEndpointsServiceCollectionExtensions
{
    /// <summary>
    /// Adds what the conventions applied by <c>WithStandards()</c> read while endpoints are built, where the problems
    /// they find are collected for <c>RequireStandards()</c>, and how standard operations are described in the OpenAPI
    /// document.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddStandardEndpoints(this IServiceCollection services)
    {
        services.TryAddSingleton<StandardEndpointProblems>();
        services.TryAddEnumerable(
            ServiceDescriptor.Transient<IApiDescriptionProvider, StandardEndpointApiDescriptionProvider>());

        return services;
    }
}
