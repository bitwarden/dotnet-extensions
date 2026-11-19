using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class MessagingOptionsValidator : IValidateOptions<MessagingOptions>
{
    public ValidateOptionsResult Validate(string? name, MessagingOptions options)
    {
        if (!string.IsNullOrEmpty(options.AzureServiceBusConnectionString) &&
            !string.IsNullOrEmpty(options.RabbitUri))
            return ValidateOptionsResult.Fail(
                $"Both {nameof(MessagingOptions.AzureServiceBusConnectionString)} and " +
                $"{nameof(MessagingOptions.RabbitUri)} are configured; " +
                "only one messaging backend can be active at a time.");

        if (options.MaxLockRenewalDuration < TimeSpan.Zero)
            return ValidateOptionsResult.Fail(
                $"{nameof(MessagingOptions.MaxLockRenewalDuration)} cannot be negative.");

        return ValidateOptionsResult.Success;
    }
}
