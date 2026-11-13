namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// What a service does with an outgoing call that would carry an event's actor as the person acting
/// (<c>OutgoingCall:*</c>).
/// </summary>
/// <remarks>
/// A consumer acts with its own authority, and the event's actor is for the record only. While a service
/// handles an event (<see cref="Context.ContextScope.IsEvent"/>), a call to another service either is the
/// service's own (<see cref="OutgoingCall.AsSystem(Guid)"/> and the other <c>AsSystem</c> statements), or names its
/// person outright (<see cref="OutgoingCall.AboutUser"/>), or carries no person because the event names none. A call
/// with no statement while the event names a person, or inside a statement that names nobody and so keeps the event's
/// person (<see cref="OutgoingCall.AboutOrganization"/>, say), is logged as a warning, or refused with this on.
/// </remarks>
public sealed class OutgoingCallOptions
{
    /// <summary>
    /// The configuration section.
    /// </summary>
    public const string SectionName = "OutgoingCall";

    /// <summary>
    /// Refuse a call that would carry an event's actor (an <see cref="InvalidOperationException"/> before it is sent)
    /// rather than only log it. Off by default, so Development and production log a warning and send the call as before.
    /// A test host turns it on, so a consumer calling on the person whose request published its event fails the test
    /// that publishes it, rather than reaching a callee that refuses the person as the calling service's word (a
    /// <c>403</c>) in production. Leave it off in Development: a refusal there breaks flows no test covers.
    /// </summary>
    public bool RefuseEventActor { get; set; }
}
