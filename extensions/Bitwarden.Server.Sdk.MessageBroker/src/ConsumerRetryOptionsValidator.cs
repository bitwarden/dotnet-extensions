using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Refuses <see cref="ConsumerRetryOptions"/> with fewer than one delivery, a negative delay, or a maximum delay shorter than the first.
/// </summary>
internal sealed class ConsumerRetryOptionsValidator : IValidateOptions<ConsumerRetryOptions>
{
    public ValidateOptionsResult Validate(string? name, ConsumerRetryOptions options)
    {
        if (options.MaxDeliveries < 1)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(ConsumerRetryOptions.MaxDeliveries)} for '{name}' must be at least one.");
        }

        if (options.RetryDelay < TimeSpan.Zero || options.MaxRetryDelay < TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(ConsumerRetryOptions.RetryDelay)} and {nameof(ConsumerRetryOptions.MaxRetryDelay)} for '{name}' cannot be negative.");
        }

        if (options.MaxRetryDelay < options.RetryDelay)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(ConsumerRetryOptions.MaxRetryDelay)} for '{name}' cannot be shorter than {nameof(ConsumerRetryOptions.RetryDelay)}.");
        }

        return ValidateOptionsResult.Success;
    }
}
