using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Validates <see cref="NegotiationOptions"/> when the process registered at least one
/// publisher (i.e., any <see cref="PublisherRoleMarker"/> is present in DI). Enforces
/// <see cref="NegotiationOptions.ServiceName"/> is set and
/// <see cref="NegotiationOptions.RequestSubscriptionName"/> resolves, so a mis-configured
/// service fails at host start rather than mid-request with an obscure resolution error.
/// <para>
/// Not wired to <c>ValidateOnStart</c> here — the validator only fires once
/// <see cref="NegotiationOptions"/> is materialized (which happens inside
/// <see cref="PublisherJoinRequester.StartAsync"/> and
/// <see cref="NegotiationListenerCoordinator.StartAsync"/> for distributed backends).
/// Channel-backend deployments never touch these options, so they never trip the validator.
/// </para>
/// </summary>
internal sealed class PublisherNegotiationOptionsValidator(IServiceProvider services)
    : IValidateOptions<NegotiationOptions>
{
    public ValidateOptionsResult Validate(string? name, NegotiationOptions options)
    {
        if (!services.GetServices<PublisherRoleMarker>().Any())
            return ValidateOptionsResult.Success;

        if (string.IsNullOrEmpty(options.ServiceName))
            return ValidateOptionsResult.Fail(
                $"{nameof(NegotiationOptions)}.{nameof(NegotiationOptions.ServiceName)} must be set " +
                "before AddPublisher can complete admission. It derives the request and reply " +
                "subscription names on the control topic.");

        try
        {
            _ = options.RequestSubscriptionName;
        }
        catch (InvalidOperationException ex)
        {
            return ValidateOptionsResult.Fail(ex.Message);
        }

        return ValidateOptionsResult.Success;
    }
}
