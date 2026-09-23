using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Validates <see cref="NegotiationOptions"/> when the process participates in negotiation as
/// a publisher, subscriber, or both (i.e., any <see cref="PublisherRoleMarker"/> or
/// <see cref="SubscriberRoleMarker"/> is present in DI). Requires
/// <see cref="NegotiationOptions.ServiceName"/> is set and the role-appropriate subscription
/// name resolves — publishers need <see cref="NegotiationOptions.RequestSubscriptionName"/>,
/// subscribers need <see cref="NegotiationOptions.ReplySubscriptionName"/> — so a
/// mis-configured service fails at host start rather than mid-request with an obscure
/// resolution error.
/// <para>
/// Not wired to <c>ValidateOnStart</c> here — the validator only fires once
/// <see cref="NegotiationOptions"/> is materialized (which happens inside
/// <see cref="PublisherJoinRequester.StartingAsync"/>,
/// <see cref="SubscriberJoinRequester.StartingAsync"/>, and
/// <see cref="NegotiationListenerCoordinator.StartingAsync"/> for distributed backends).
/// Channel-backend deployments never touch these options, so they never trip the validator.
/// </para>
/// </summary>
internal sealed class NegotiationRoleOptionsValidator(IServiceProvider services)
    : IValidateOptions<NegotiationOptions>
{
    public ValidateOptionsResult Validate(string? name, NegotiationOptions options)
    {
        var hasPublisher = services.GetServices<PublisherRoleMarker>().Any();
        var hasSubscriber = services.GetServices<SubscriberRoleMarker>().Any();
        if (!hasPublisher && !hasSubscriber)
            return ValidateOptionsResult.Success;

        if (string.IsNullOrEmpty(options.ServiceName))
            return ValidateOptionsResult.Fail(
                $"{nameof(NegotiationOptions)}.{nameof(NegotiationOptions.ServiceName)} must be set " +
                "before AddPublisher or AddSubscriber can complete admission. It derives the request " +
                "and reply subscription names on the control topic.");

        try
        {
            if (hasPublisher) _ = options.RequestSubscriptionName;
            if (hasSubscriber) _ = options.ReplySubscriptionName;
        }
        catch (InvalidOperationException ex)
        {
            return ValidateOptionsResult.Fail(ex.Message);
        }

        return ValidateOptionsResult.Success;
    }
}
