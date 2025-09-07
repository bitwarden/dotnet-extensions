using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
#if BIT_INCLUDE_TELEMETRY
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
#endif

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extensions for <see cref="IHostBuilder"/>.
/// </summary>
public static class HostBuilderExtensions
{
    const string SelfHostedConfigKey = "globalSettings:selfHosted";

    /// <summary>
    /// Configures the host to use Bitwarden defaults.
    /// </summary>
    /// <typeparam name="TBuilder"></typeparam>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static TBuilder UseBitwardenSdk<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Configuration.GetValue(SelfHostedConfigKey, false))
        {
            AddSelfHostedConfig(builder.Configuration, builder.Environment);
        }

        AddMetrics(builder.Services);
#if BIT_INCLUDE_FEATURES
        builder.Services.AddFeatureFlagServices(builder.Configuration);
#endif

        return builder;
    }

    /// <summary>
    /// Configures the host to use Bitwarden defaults.
    /// </summary>
    /// <param name="hostBuilder">Host builder.</param>
    /// <param name="bitwardenHostOptions">Host options.</param>
    /// <returns></returns>
    public static IHostBuilder UseBitwardenSdk(this IHostBuilder hostBuilder)
    {
        hostBuilder.ConfigureAppConfiguration((context, builder) =>
        {
            if (context.Configuration.GetValue(SelfHostedConfigKey, false))
            {
                AddSelfHostedConfig(builder, context.HostingEnvironment);
            }
        });

        hostBuilder.ConfigureServices((_, services) =>
        {
            AddMetrics(services);
        });

#if BIT_INCLUDE_FEATURES
        hostBuilder.ConfigureServices((context, services) =>
        {
            services.AddFeatureFlagServices(context.Configuration);
        });
#endif

        return hostBuilder;
    }

    private static void AddSelfHostedConfig(IConfigurationBuilder configurationBuilder, IHostEnvironment environment)
    {
        // Current ordering of Configuration:
        // 1. Chained (from Host config)
        //      1. Memory
        //      2. Memory
        //      3. Environment (DOTNET_)
        //      4. Chained
        //          1. Memory
        //          2. Environment (ASPNETCORE_)
        // 2. Json (appsettings.json)
        // 3. Json (appsettings.Environment.json)
        // 4. Secrets
        // 5. Environment (*)
        // 6. Command line args, if present
        // vv If selfhosted vv
        // 7. Json (appsettings.json) again
        // 8. Json (appsettings.Environment.json)
        // 9. Secrets (if development)
        // 10. Environment (*)
        // 11. Command line args, if present

        // As you can see there was a lot of doubling up,
        // I would rather insert the self-hosted config, when necessary into
        // the index.

        // These would fail if two main things happen, the default host setup from .NET changes
        // and a new source is added before the appsettings ones.
        // or someone change the order or adding this helper, both things I believe would be quickly
        // discovered during development.

        var sources = configurationBuilder.Sources;

        // I expect the 3rd source to be the main appsettings.json file
        Debug.Assert(sources[2] is FileConfigurationSource mainJsonSource
            && mainJsonSource.Path == "appsettings.json");
        // I expect the 4th source to be the environment specific json file
        Debug.Assert(sources[3] is FileConfigurationSource environmentJsonSource
            && environmentJsonSource.Path == $"appsettings.{environment.EnvironmentName}.json");

        // If both of those are true, I feel good about inserting our own self-hosted config after
        configurationBuilder.Sources.Insert(4, new JsonConfigurationSource
        {
            Path = "appsettings.SelfHosted.json",
            Optional = true,
            ReloadOnChange = true
        });

        if (environment.IsDevelopment())
        {
            var appAssembly = Assembly.Load(new AssemblyName(environment.ApplicationName));
            configurationBuilder.AddUserSecrets(appAssembly, optional: true);
        }

        configurationBuilder.AddEnvironmentVariables();
    }


    private static void AddMetrics(IServiceCollection services)
    {
#if BIT_INCLUDE_TELEMETRY
        services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddOtlpExporter();

                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddOtlpExporter();

                tracing.AddAspNetCoreInstrumentation();
                tracing.AddHttpClientInstrumentation();
                tracing.AddEntityFrameworkCoreInstrumentation();
            });
#endif
    }
}
