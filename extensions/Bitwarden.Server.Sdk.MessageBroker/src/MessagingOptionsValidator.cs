using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Refuses <see cref="MessagingOptions"/> that select more than one broker.
/// </summary>
internal sealed class MessagingOptionsValidator : IValidateOptions<MessagingOptions>
{
    public ValidateOptionsResult Validate(string? name, MessagingOptions options)
    {
        if (options.UsesAzureServiceBus && !string.IsNullOrEmpty(options.RabbitUri))
        {
            return ValidateOptionsResult.Fail(
                "Both Azure Service Bus and " +
                $"{nameof(MessagingOptions.RabbitUri)} are configured; " +
                "only one messaging backend can be active at a time.");
        }

        if (!string.IsNullOrEmpty(options.AzureServiceBusConnectionString) &&
            !string.IsNullOrEmpty(options.AzureServiceBusFullyQualifiedNamespace))
        {
            return ValidateOptionsResult.Fail(
                $"Both {nameof(MessagingOptions.AzureServiceBusConnectionString)} and " +
                $"{nameof(MessagingOptions.AzureServiceBusFullyQualifiedNamespace)} are configured; " +
                "set the connection string for a shared access key or the namespace for a token credential, not both.");
        }

        if (options.MaxLockRenewalDuration < TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(MessagingOptions.MaxLockRenewalDuration)} cannot be negative.");
        }

        return ValidateOptionsResult.Success;
    }
}
