using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
#if BIT_INCLUDE_TELEMETRY
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
#endif
#if BIT_INCLUDE_CACHING
using Bitwarden.Server.Sdk.Caching;
#endif
#if BIT_INCLUDE_TELEMETRY && BIT_INCLUDE_ASPIRE_INTEGRATION
using OpenTelemetry.Logs;
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

#if BIT_INCLUDE_AUTHENTICATION
        builder.Services.AddBitwardenAuthentication();
#endif

#if BIT_INCLUDE_CACHING
        builder.Services.AddBitwardenCaching();
#endif

#if BIT_INCLUDE_WEB_ESSENTIALS
        builder.Services.AddWebEssentials();
#endif

#if BIT_INCLUDE_ENVIRONMENT
        builder.Services.AddBitwardenEnvironment();
#endif

#if BIT_INCLUDE_ASPIRE_INTEGRATION
        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddServiceDiscovery();
        });
#endif

        return builder;
    }

    /// <summary>
    /// Configures the host to use Bitwarden defaults.
    /// </summary>
    /// <param name="hostBuilder">Host builder.</param>
    /// <returns></returns>
    public static IHostBuilder UseBitwardenSdk(this IHostBuilder hostBuilder)
    {
        hostBuilder.ConfigureAppConfiguration((context, builder) =>
        {
            // Legacy entrypoint does not yet have access to configuration with all environment variables
            // so we need to search the environment ourselves.
            if (string.Equals(Environment.GetEnvironmentVariable("globalSettings__selfHosted"), "true", StringComparison.OrdinalIgnoreCase))
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

#if BIT_INCLUDE_AUTHENTICATION
        hostBuilder.ConfigureServices((_, services) =>
        {
            services.AddBitwardenAuthentication();
        });
#endif

#if BIT_INCLUDE_CACHING
        hostBuilder.ConfigureServices((_, services) =>
        {
            services.AddBitwardenCaching();
        });
#endif

#if BIT_INCLUDE_WEB_ESSENTIALS
        hostBuilder.ConfigureServices((_, services) =>
        {
            services.AddWebEssentials();
        });
#endif

#if BIT_INCLUDE_ENVIRONMENT
        hostBuilder.ConfigureServices((_, services) =>
        {
            services.AddBitwardenEnvironment();
        });
#endif

#if BIT_INCLUDE_ASPIRE_INTEGRATION
        hostBuilder.ConfigureServices((_, services) =>
        {
            services.AddServiceDiscovery();

            services.ConfigureHttpClientDefaults(http =>
            {
                http.AddServiceDiscovery();
            });
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

                // Auto-detect the DD_ENV environment variable we used for DD instrumentation and add a resource
                // attribute for it so that we can get better trace based metrics.
                if (Environment.GetEnvironmentVariable("DD_ENV") is { } ddEnv)
                {
                    r.AddAttributes(new Dictionary<string, object>
                    {
                        ["env"] = ddEnv,
                    });
                }
            })
#if BIT_INCLUDE_ASPIRE_INTEGRATION
            .WithLogging(loggingBuilder =>
            {
                loggingBuilder.AddOtlpExporter();
            },
            loggingOptions =>
            {
                loggingOptions.IncludeFormattedMessage = true;
                loggingOptions.IncludeScopes = true;
            })
#endif
            .WithMetrics(metrics =>
            {
                if (configuration.GetValue<bool>("OpenTelemetry:Metrics:Enabled", openTelemetryEnabled))
                {
                    metrics.AddOtlpExporter();
                }

                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();
                metrics.AddSqlClientInstrumentation();

                metrics.AddMeter("Bitwarden.*");
                metrics.AddMeter("Bit.*");

#if BIT_INCLUDE_CACHING
                metrics.AddFusionCacheInstrumentation();
#endif
            })
            .WithTracing(tracing =>
            {
                if (configuration.GetValue<bool>("OpenTelemetry:Tracing:Enabled", openTelemetryEnabled))
                {
                    tracing.AddOtlpExporter();
                }

                // Record exceptions as span events so the type, message, and stack trace are visible on failed spans.
                tracing.AddAspNetCoreInstrumentation(options => options.RecordException = true);
                // The .NET 10 runtime records exceptions on HttpClient spans itself, recording them again would duplicate the event.
                tracing.AddHttpClientInstrumentation(options => options.RecordException = Environment.Version.Major < 10);
                tracing.AddEntityFrameworkCoreInstrumentation();
                tracing.AddSqlClientInstrumentation(options => options.RecordException = true);

#if BIT_INCLUDE_CACHING
                tracing.AddFusionCacheInstrumentation();
                tracing.AddRedisInstrumentation();
#endif
                tracing.AddSource("Bitwarden.*");
                tracing.AddSource("Azure.*");
            });

        // Health check endpoints are called constantly by orchestrators, tracing them adds noise and cost without
        // telling us anything. They are filtered when the request starts, which also drops any child spans, so failed
        // health checks are not traced either.
        // PostConfigure so that we compose with any Filter the application configures itself.
        services.PostConfigure<OpenTelemetry.Instrumentation.AspNetCore.AspNetCoreTraceInstrumentationOptions>(options =>
        {
            var filter = options.Filter;
            options.Filter = context => !IsHealthCheckRequest(context.Request) && (filter == null || filter(context));
        });

#if BIT_INCLUDE_CACHING
        // The caching library uses RedisCacheOptions for customizing how redis is used.
        // To make that work with open telemetry instrumentation we need access to the connection
        // to make it get tracked, so switch uses of the Configuration property with ConnectionMultiplexerFactory
        // which takes precedence. Ref: https://github.com/dotnet/aspnetcore/blob/eec80e1c63963802a215d58b3907d6e46090fc28/src/Caching/StackExchangeRedis/src/RedisCache.cs#L336-L343
        services.AddSingleton<Microsoft.Extensions.Options.IPostConfigureOptions<Microsoft.Extensions.Caching.StackExchangeRedis.RedisCacheOptions>>(sp =>
        {
            return new Microsoft.Extensions.Options.PostConfigureOptions<Microsoft.Extensions.Caching.StackExchangeRedis.RedisCacheOptions>(name: null, options =>
            {
                if (!string.IsNullOrEmpty(options.Configuration))
                {
                    var instrumentation = sp.GetRequiredService<OpenTelemetry.Instrumentation.StackExchangeRedis.StackExchangeRedisInstrumentation>();
                    options.ConnectionMultiplexerFactory = async () =>
                    {
                        var connection = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(options.Configuration);
                        instrumentation.AddConnection(connection);
                        return connection;
                    };
                }
            });
        });
#endif
#endif
    }

#if BIT_INCLUDE_TELEMETRY
    private static bool IsHealthCheckRequest(Microsoft.AspNetCore.Http.HttpRequest request)
    {
        return Microsoft.AspNetCore.Http.HttpMethods.IsGet(request.Method)
            && (request.Path.Equals("/alive", StringComparison.OrdinalIgnoreCase)
                || request.Path.Equals("/healthz", StringComparison.OrdinalIgnoreCase));
    }
#endif
}
