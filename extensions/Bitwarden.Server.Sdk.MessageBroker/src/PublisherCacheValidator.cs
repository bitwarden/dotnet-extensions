using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Verifies that <see cref="ZiggyCreatures.Caching.Fusion.IFusionCache"/> is registered whenever
/// a cross-process messaging backend (Azure Service Bus, Rabbit) is configured. Version
/// negotiation stores its fleet-state snapshot in a shared cache; without one, admission
/// decisions silently see an empty fleet.
/// <para>
/// Fires at host start via <c>ValidateOnStart</c> so a caller who binds
/// <see cref="MessagingOptions"/> after <c>AddPublisher</c> — bypassing the eager
/// auto-registration — gets a clear error rather than a downstream resolution failure.
/// </para>
/// </summary>
internal sealed class PublisherCacheValidator(IServiceProvider services) : IValidateOptions<MessagingOptions>
{
    public ValidateOptionsResult Validate(string? name, MessagingOptions options)
    {
        var usesDistributedBackend = !string.IsNullOrEmpty(options.AzureServiceBusConnectionString)
                                     || !string.IsNullOrEmpty(options.RabbitUri);
        if (!usesDistributedBackend)
            return ValidateOptionsResult.Success;

        var isService = services.GetRequiredService<IServiceProviderIsService>();
        if (isService.IsService(typeof(IConfigureOptions<FusionCacheOptions>)))
            return ValidateOptionsResult.Success;

        return ValidateOptionsResult.Fail(
            "Azure Service Bus or Rabbit is configured but Bitwarden caching has not been added. " +
            "Call services.AddBitwardenCaching() anywhere in your DI setup. Version negotiation " +
            "stores its fleet-state snapshot in a shared IFusionCache; without it, admission " +
            "decisions silently see an empty fleet.");
    }
}
