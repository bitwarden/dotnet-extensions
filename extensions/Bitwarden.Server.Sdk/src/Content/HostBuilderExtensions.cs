using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
#if BIT_INCLUDE_TELEMETRY
using Bitwarden.Server.Sdk.Internal;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
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

        AddMetrics(builder.Services, builder.Configuration, builder.Environment);
#if BIT_INCLUDE_FEATURES
        builder.Services.AddFeatureFlagServices();
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

        hostBuilder.ConfigureServices((context, services) =>
        {
            AddMetrics(services, context.Configuration, context.HostingEnvironment);
        });

#if BIT_INCLUDE_FEATURES
        hostBuilder.ConfigureServices((_, services) =>
        {
            services.AddFeatureFlagServices();
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

        var i = 0;
        for (; i < sources.Count; i++)
        {
            if (sources[i] is FileConfigurationSource jsonSource && jsonSource.Path == $"appsettings.{environment.EnvironmentName}.json")
            {
                break;
            }
        }

        // If both of those are true, I feel good about inserting our own self-hosted config after
        configurationBuilder.Sources.Insert(i + 1, new JsonConfigurationSource
        {
            Path = "appsettings.SelfHosted.json",
            Optional = true,
            ReloadOnChange = true
        });
    }

    private static void AddMetrics(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
#if BIT_INCLUDE_TELEMETRY
        const string OtelDebuggingConfigKey = "OTEL_DEBUGGING";

        // Allow the exporting of telemetry to be disabled through a configuration key
        // but if the setting isn't present default to enabled for cloud uses
        // and disabled for self hosted installations.
        var openTelemetryEnabled = configuration.GetValue<bool>(
            "OpenTelemetry:Enabled",
            !configuration.GetValue(SelfHostedConfigKey, false)
        );

        services.AddOpenTelemetry()
            .ConfigureResource(r =>
            {
                r.AddService(
                    serviceName: hostEnvironment.ApplicationName
                );
                r.AddTelemetrySdk();
                r.AddEnvironmentVariableDetector();
            })
            .WithMetrics(metrics =>
            {
                if (configuration.GetValue<bool>("OpenTelemetry:Metrics:Enabled", openTelemetryEnabled))
                {
                    metrics.AddOtlpExporter();
                }

                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();

                metrics.AddMeter("Bitwarden.*");
                metrics.AddMeter("Bit.*");
            })
            .WithTracing(tracing =>
            {
                if (configuration.GetValue<bool>("OpenTelemetry:Tracing:Enabled", openTelemetryEnabled))
                {
                    tracing.AddOtlpExporter();
                }

                tracing.AddAspNetCoreInstrumentation();
                tracing.AddHttpClientInstrumentation();
                tracing.AddEntityFrameworkCoreInstrumentation();
            });

        if (configuration.GetValue(OtelDebuggingConfigKey, false))
        {
            if (!services.Any((sd) => sd.ServiceType == typeof(IHostedService) && sd.ImplementationType == typeof(OtelDebuggingHostedService)))
            {
                services.Insert(0, ServiceDescriptor.Singleton<IHostedService, OtelDebuggingHostedService>());
            }
        }
#endif
    }
}
