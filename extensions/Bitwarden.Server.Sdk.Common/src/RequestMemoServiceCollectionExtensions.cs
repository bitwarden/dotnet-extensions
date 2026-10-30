using Bitwarden.Server.Sdk.Common;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering <see cref="RequestMemo"/>.
/// </summary>
public static class RequestMemoServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="RequestMemo"/> for each request.
    /// </summary>
    /// <remarks>
    /// This method is safe to be called multiple times.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddRequestMemo(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<RequestMemo>();

        return services;
    }
}
