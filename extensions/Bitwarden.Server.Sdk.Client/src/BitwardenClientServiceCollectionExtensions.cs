using System.Diagnostics.CodeAnalysis;
using Bitwarden.Server.Sdk.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers a typed client for another Bitwarden service.
/// </summary>
/// <remarks>
/// Called by the client package a service publishes, not by a consumer. A consumer calls that
/// package's own <c>Add{Service}Client</c>, which supplies the service's name and the scopes it
/// requires and leaves everything else to convention.
/// </remarks>
public static class BitwardenClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TImplementation"/> as the client for a service.
    /// </summary>
    /// <typeparam name="TClient">The interface a consumer injects.</typeparam>
    /// <typeparam name="TImplementation">The client behind it.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceName">
    /// The service being called, as it appears in configuration: <c>Reference</c> binds
    /// <c>Services:Reference</c>.
    /// </param>
    /// <param name="scopes">
    /// The scopes the client needs. Declared by the client package. Where more than one client is registered for the
    /// same service, the token for it carries the scopes of all of them.
    /// </param>
    /// <param name="configure">
    /// Applied after the conventions, so a consumer can override anything conventional; only
    /// <c>Services:{Name}:Timeout</c>, <c>:Retries</c> and <c>:RetryDelay</c>, where a deployment sets them, apply after it.
    /// </param>
    public static IHttpClientBuilder AddBitwardenClient<TClient,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
        this IServiceCollection services,
        string serviceName,
        IReadOnlyList<string> scopes,
        Action<BitwardenClientOptions>? configure = null)
        where TClient : class
        where TImplementation : class, TClient
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        // Read once: the operations the callee's contract, embedded in the client package, marks as honouring keys.
        var keyed = KeyedOperations.FromAssembly(typeof(TImplementation).Assembly);

        // Bounded for every token request, a renewal no call waits on included; a call's deadline bounds it too.
        services.AddHttpClient(ServiceTokenProvider.TokenClientName, client => client.Timeout = ServiceTokenProvider.RequestTimeout);

        // Whether a call carrying an event's actor is refused (OutgoingCall:RefuseEventActor, EventActor.cs), bound once
        // however many clients a service registers.
        services.TryAddSingleton<IConfigureOptions<OutgoingCallOptions>>(provider =>
            new ConfigureNamedOptions<OutgoingCallOptions>(Options.Options.DefaultName, options =>
                options.RefuseEventActor = bool.TryParse(
                    provider.GetRequiredService<IConfiguration>()[$"{OutgoingCallOptions.SectionName}:{nameof(OutgoingCallOptions.RefuseEventActor)}"],
                    out var refuse) && refuse));

        // A service's clients share one set of options, so a second client for the same service adds to what the first
        // set rather than starting again: the conventions and the validator once, the scopes of every client together
        // (one token serves them all), and each client's own overrides in the order they were registered.
        var first = !services.Any(descriptor =>
            !descriptor.IsKeyedService
            && descriptor.ImplementationInstance is BitwardenClientOptionsValidator validator
            && validator.ServiceName == serviceName);

        var builder = services.AddOptions<BitwardenClientOptions>(serviceName);

        if (first)
        {
            builder.Configure<IConfiguration>((options, configuration) => Conventions.Apply(options, configuration, serviceName));

            services.AddSingleton<IValidateOptions<BitwardenClientOptions>>(
                new BitwardenClientOptionsValidator(serviceName));
        }

        builder
            .Configure<IConfiguration>((options, configuration) =>
            {
                options.Scopes = [.. options.Scopes.Union(scopes, StringComparer.Ordinal)];
                configure?.Invoke(options);
                Conventions.ApplyTiming(options, configuration, serviceName);
            })
            // At startup, not on the first call: a service missing a downstream address should
            // fail to start rather than fail the first request that needed it.
            .ValidateOnStart();

        // Keyed, and by interface: a test substitutes its own for this service without
        // disturbing any other client the same application registers.
        services.TryAddKeyedSingleton<IServiceTokenProvider>(serviceName, (provider, key) =>
            new ServiceTokenProvider(
                provider.GetRequiredService<IHttpClientFactory>(),
                provider.GetRequiredService<IOptionsMonitor<BitwardenClientOptions>>().Get((string)key!),
                provider.GetRequiredService<ILogger<ServiceTokenProvider>>(),
                provider.GetService<TimeProvider>()));

        // Keyed by the callee, so every client of it in the process shares what it has learned of it.
        services.TryAddKeyedSingleton(serviceName, (provider, key) =>
        {
            var options = provider.GetRequiredService<IOptionsMonitor<BitwardenClientOptions>>().Get((string)key!);
            return new CalleeCircuit(options.BreakAfter, options.BreakFor, provider.GetService<TimeProvider>() ?? TimeProvider.System);
        });

        return services.AddHttpClient<TClient, TImplementation>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptionsMonitor<BitwardenClientOptions>>().Get(serviceName);

                client.BaseAddress = new Uri(options.BaseAddress.TrimEnd('/') + "/");

                // Enforced by the fault handler instead, which can tell the dependency not
                // answering from the caller giving up. Left here too, it would cut in first and
                // hide which of the two it was.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            // In this order, outermost first (IHttpClientFactory runs handlers in the order they are added):
            //
            // 1. The fault handler: the call's one deadline, around everything below, and the translation of every
            //    failure into the exception it means.
            // 2. The idempotency key, once, before any attempt, so every attempt of a write carries the same one.
            //    Then the write reported to the operation running now, once per key (ServiceWriteHandler).
            // 3. The retries, within the deadline.
            // 4. and 5. The token and the context token, obtained from Identity for each attempt (held between them).
            // 6. The circuit breaker, innermost, so it counts attempts at the callee itself and never Identity's
            //    failures.
            .AddHttpMessageHandler(provider => new ServiceFaultHandler(
                serviceName,
                provider.GetRequiredService<IOptionsMonitor<BitwardenClientOptions>>().Get(serviceName).Timeout))
            .AddHttpMessageHandler(() => new IdempotencyKeyHandler(keyed))
            .AddHttpMessageHandler(provider => new ServiceWriteHandler(
                serviceName,
                [.. provider.GetRequiredService<IOptionsMonitor<BitwardenClientOptions>>().Get(serviceName).ReadingPaths]))
            .AddHttpMessageHandler(provider =>
            {
                var options = provider.GetRequiredService<IOptionsMonitor<BitwardenClientOptions>>().Get(serviceName);
                return new RetryHandler(options.Retries, options.RetryDelay, provider.GetService<TimeProvider>() ?? TimeProvider.System);
            })
            .AddHttpMessageHandler(provider =>
                new ServiceTokenHandler(provider.GetRequiredKeyedService<IServiceTokenProvider>(serviceName)))
            .AddHttpMessageHandler(provider => new ContextHandler(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredKeyedService<IServiceTokenProvider>(serviceName)))
            .AddHttpMessageHandler(provider =>
                new CircuitBreakerHandler(serviceName, provider.GetRequiredKeyedService<CalleeCircuit>(serviceName)));
    }
}
