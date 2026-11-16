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

        if (options.RetryDelay < TimeSpan.Zero || options.MaxRetryDelay < TimeSpan.Zero)
            return ValidateOptionsResult.Fail(
                $"{nameof(MessagingOptions.RetryDelay)} and {nameof(MessagingOptions.MaxRetryDelay)} cannot be negative.");

        if (options.MaxLockRenewalDuration < options.MaxRetryDelay)
            return ValidateOptionsResult.Fail(
                $"{nameof(MessagingOptions.MaxLockRenewalDuration)} cannot be shorter than {nameof(MessagingOptions.MaxRetryDelay)}: " +
                "a message waiting to be retried would lose its lock.");

        if (options.MaxRetryDelay < options.RetryDelay)
            return ValidateOptionsResult.Fail(
                $"{nameof(MessagingOptions.MaxRetryDelay)} cannot be shorter than {nameof(MessagingOptions.RetryDelay)}.");

        return ValidateOptionsResult.Success;
    }
}
