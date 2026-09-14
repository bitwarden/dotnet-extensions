using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Verifies that <see cref="ZiggyCreatures.Caching.Fusion.IFusionCache"/> is registered whenever
/// a cross-process messaging backend (Azure Service Bus, Rabbit) is configured. Fires at host
/// start via <c>ValidateOnStart</c> so a caller who binds <see cref="MessagingOptions"/> after
/// <c>AddPublisher</c> — bypassing the eager auto-registration — gets a clear error rather than
/// a downstream resolution failure.
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
            "Call services.AddBitwardenCaching() anywhere in your DI setup. Publishers of a " +
            "cross-process backend need a shared IFusionCache to ensure subscriber compatibility.");
    }
}
