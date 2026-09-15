using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class NegotiationOptionsValidator : IValidateOptions<NegotiationOptions>
{
    public ValidateOptionsResult Validate(string? name, NegotiationOptions options)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ServiceName))
            errors.Add(
                $"{nameof(NegotiationOptions.ServiceName)} must be set. It is used to derive " +
                "the publisher-control and reply subscription names on the control topic; the " +
                "resulting names must match deployed infrastructure.");
        if (string.IsNullOrWhiteSpace(options.ProcessDisplayName))
            errors.Add(
                $"{nameof(NegotiationOptions.ProcessDisplayName)} must be set. It is the " +
                "operator-facing label for this process and forms the human-readable half of " +
                $"{nameof(NegotiationOptions.InstanceId)}.");
        if (string.IsNullOrWhiteSpace(options.ControlTopicName))
            errors.Add($"{nameof(NegotiationOptions.ControlTopicName)} must not be empty.");
        if (options.HeartbeatInterval <= TimeSpan.Zero)
            errors.Add($"{nameof(NegotiationOptions.HeartbeatInterval)} must be positive.");
        if (options.TtlPaddingFactor <= 1.0)
            errors.Add(
                $"{nameof(NegotiationOptions.TtlPaddingFactor)} must be greater than 1.0 so the " +
                "cache entry TTL exceeds the heartbeat interval.");
        if (options.AdmissionTimeout <= TimeSpan.Zero)
            errors.Add($"{nameof(NegotiationOptions.AdmissionTimeout)} must be positive.");
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
