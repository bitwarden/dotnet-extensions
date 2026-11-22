using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// On the health port, the probes alone: any other request is not found there, before any middleware sees it.
/// </summary>
internal sealed class ProbesOnlyOnHealthPort(int port) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (context.Connection.LocalPort == port && !IsProbe(context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            return nextMiddleware(context);
        });
        next(app);
    };

    private static bool IsProbe(PathString path) =>
        SystemEndpoints.ProbePaths.Any(probe => path.Equals(probe, StringComparison.OrdinalIgnoreCase));
}
