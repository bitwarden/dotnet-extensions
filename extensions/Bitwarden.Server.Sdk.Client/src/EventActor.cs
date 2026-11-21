using Bitwarden.Server.Sdk.Context;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Holds a call made while handling an event to the rule that the event's actor is for the record only.
/// </summary>
/// <remarks>
/// <para>
/// A host handling an event establishes what the event says as the context of handling it
/// (<see cref="ContextScope.BeginForEvent"/>): the person it names, whoever's request published it, is recorded there
/// (<see cref="IContext.RecordedUserId"/>), never acting, so a call with no statement is the service's own. A call that
/// carried them as the person acting would be this service's word about that person (an asserted context), which a
/// callee accepts only where it declares it does for this service. So while an event is handled, a call is the
/// service's own (no statement, or an <c>AsSystem</c> one), or names its person outright
/// (<see cref="OutgoingCall.AboutUser"/>, <see cref="OutgoingCall.About"/>).
/// </para>
/// <para>
/// A statement that names no person but is not the system's (<see cref="OutgoingCall.AboutOrganization"/>,
/// <see cref="OutgoingCall.AboutOrganizationWithoutProvider"/>, <see cref="OutgoingCall.WithoutProvider"/>) speaks for
/// whoever is acting, and while an event is handled nobody is: it is held wrong, as an <c>AsSystem</c> statement says
/// what such a call means. The rule holds whatever the event names, so a test publishing an event with an empty context
/// catches a statement as surely as one published in a person's request.
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

        return stated is not null
            ? "A call made while handling an event is inside an OutgoingCall statement that names no person and is "
                + "not the system's (AboutOrganization, AboutOrganizationWithoutProvider or WithoutProvider), so it speaks "
                + "for whoever is acting, and while an event is handled nobody is. " + Remedy
            : null;
    }

    private const string Remedy =
        "A consumer acts with its own authority, and the event's actor is for the record only. "
        + "Make the call the service's own with no statement, or with OutgoingCall.AsSystem(organization), AsSystem(), AsSystemAbout(user) "
        + "or AsSystemForProvider(provider); name a person with OutgoingCall.AboutUser only where the callee declares "
        + "[AcceptsAssertedContext] for this service.";
}
