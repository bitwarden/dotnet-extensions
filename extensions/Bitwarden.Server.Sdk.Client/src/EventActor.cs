using Bitwarden.Server.Sdk.Context;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Holds a call made while handling an event to the rule that the event's actor is for the record only.
/// </summary>
/// <remarks>
/// <para>
/// A host handling an event establishes what the event says as the context of handling it
/// (<see cref="ContextScope.BeginForEvent"/>), so the person it names is whoever's request published it. A call that
/// carries them as the person acting is this service's word about that person (an asserted context), which a callee
/// accepts only where it declares it does for this service, and which says the consumer acts for somebody it has no
/// request from. So while an event is handled, a call is the service's own (an <c>AsSystem</c> statement), or names
/// its person outright (<see cref="OutgoingCall.AboutUser"/>, <see cref="OutgoingCall.About"/>), or carries no person
/// because the event names none.
/// </para>
/// <para>
/// A statement that names no person but is not the system's (<see cref="OutgoingCall.AboutOrganization"/>,
/// <see cref="OutgoingCall.AboutOrganizationWithoutProvider"/>, <see cref="OutgoingCall.WithoutProvider"/>) keeps the
/// event's person by construction, so it is held wrong while an event is handled even when this one names nobody: a
/// test publishing an event with an empty context then catches what an event published in a person's request breaks.
/// </para>
/// </remarks>
internal static class EventActor
{
    /// <summary>
    /// Why a call is wrong under the rule, or null when it is right or no event is being handled.
    /// </summary>
    /// <param name="stated">What an <see cref="OutgoingCall"/> statement says of the call, if any.</param>
    public static string? Misuse(OutgoingCall.Stated? stated)
    {
        if (!ContextScope.IsEvent || stated is { System: true } || stated?.User is not null)
        {
            return null;
        }

        if (stated is not null)
        {
            return "A call made while handling an event is inside an OutgoingCall statement that names no person and is "
                + "not the system's (AboutOrganization, AboutOrganizationWithoutProvider or WithoutProvider), so it keeps "
                + "the person whose request published the event as the one acting. " + Remedy;
        }

        return ContextScope.Current?.UserId is not null
            ? "A call made while handling an event carries the person whose request published the event as the one "
                + "acting, because no OutgoingCall statement says otherwise. " + Remedy
            : null;
    }

    private const string Remedy =
        "A consumer acts with its own authority, and the event's actor is for the record only: the callee "
        + "would take the person as this service's word and refuse it (403) unless it accepts that from this service. "
        + "Make the call the service's own with OutgoingCall.AsSystem(organization), AsSystem(), AsSystemAbout(user) "
        + "or AsSystemForProvider(provider); name a person with OutgoingCall.AboutUser only where the callee declares "
        + "[AcceptsAssertedContext] for this service.";
}
