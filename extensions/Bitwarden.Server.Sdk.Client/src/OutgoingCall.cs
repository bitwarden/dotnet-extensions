namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// States whom, and which organization, one outgoing call is about, where the request being
/// handled does not say.
/// </summary>
/// <remarks>
/// <para>
/// A domain service sometimes has to ask another service a question about one particular person
/// or organization that the request it is handling does not name: signing somebody in, it asks
/// which organizations <em>they</em> belong to; listing a person's items across their
/// organizations, it asks about each organization in turn. Without the person on the call the
/// callee can only refuse it, or answer for everybody. This states the person (and, where needed,
/// the organization) outright, for that call, so the read is about one named party and says so in
/// the callee's logs and traces.
/// </para>
/// <para>
/// Narrow on purpose, and separate from <c>ContextScope</c>:
/// </para>
/// <list type="bullet">
///   <item><description>
///     It is consulted only by the outgoing <c>Bitwarden-User</c>, <c>Bitwarden-Organization</c>
///     and <c>Bitwarden-Context</c> headers, through <c>ContextHandler</c>.
///     <c>IContext</c> never sees it, so it cannot change who the service itself believes it is
///     serving, which rows its own row-level security admits, or any authorization it does on
///     its own account. <c>ContextScope</c> stays ignored inside a request, for the reason it
///     gives: a scope that changed the request's own context would let code substitute what the
///     caller named.
///   </description></item>
///   <item><description>
///     It covers <b>one</b> call. The first call made inside it consumes it, and a second call
///     inside the same statement throws rather than quietly carrying the person somewhere nobody
///     meant. Wrap the single call that is about them, nothing more.
///   </description></item>
///   <item><description>
///     Only what is stated changes. Whatever is not stated (the provider, the originating client,
///     and the user or organization when only the other is named) is carried as it would have
///     been.
///   </description></item>
///   <item><description>
///     <see cref="AsSystem(Guid)"/> and <see cref="AsSystemAbout"/> remove rather than name: the
///     call is the service's own, with no person acting (or only the one it is about) and no
///     provider (but the one <see cref="AsSystemForProvider"/> names), and its credential travels as
///     <c>SYSTEM</c>. The callee then applies the rules it keeps for a call
///     nobody is making for anyone.
///   </description></item>
///   <item><description>
///     A product service may not use it. There the acting user is the person whose session the
///     request presents and the organization the one the session was issued for; naming anyone else on a call made for them would let product code act as somebody,
///     or somewhere, the session never named. The call throws. The exceptions are
///     <see cref="AboutCreatedOrganization"/>, on an operation that creates an organization, and
///     <see cref="WithoutProvider"/>, which names nobody and only takes away.
///   </description></item>
/// </list>
/// <para>
/// A statement proves nothing about the person: it is this service's word. What it buys is a call
/// that is honest about whom it concerns.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using (OutgoingCall.AboutUser(person))
/// {
///     memberships = await organizations.ListMembershipsAsync(userId: person, cancellationToken: cancellationToken);
/// }
/// </code>
/// </example>
public static class OutgoingCall
{
    private static readonly AsyncLocal<Statement?> Ambient = new();

    /// <summary>
    /// States <paramref name="user"/> as the acting user of the next outgoing call made before the
    /// returned value is disposed.
    /// </summary>
    /// <param name="user">The person the call is about.</param>
    /// <returns>Ends the statement, restoring whatever was stated before, when disposed.</returns>
    /// <exception cref="ArgumentException"><paramref name="user"/> is empty.</exception>
    public static IDisposable AboutUser(Guid user) => Begin(Check(user, nameof(user)), null);

    /// <summary>
    /// States <paramref name="organization"/> as the organization of the next outgoing call made
    /// before the returned value is disposed.
    /// </summary>
    /// <param name="organization">The organization the call is about.</param>
    /// <returns>Ends the statement, restoring whatever was stated before, when disposed.</returns>
    /// <exception cref="ArgumentException"><paramref name="organization"/> is empty.</exception>
    public static IDisposable AboutOrganization(Guid organization) =>
        Begin(null, Check(organization, nameof(organization)));

    /// <summary>
    /// States both the acting user and the organization of the next outgoing call made before the
    /// returned value is disposed.
    /// </summary>
    /// <param name="user">The person the call is about.</param>
    /// <param name="organization">The organization the call is about.</param>
    /// <returns>Ends the statement, restoring whatever was stated before, when disposed.</returns>
    /// <exception cref="ArgumentException">Either is empty.</exception>
    public static IDisposable About(Guid user, Guid organization) =>
        Begin(Check(user, nameof(user)), Check(organization, nameof(organization)));

    /// <summary>
    /// States the organization the request being handled has just created as the organization of the
    /// next outgoing call: the one use a product service may make of this class.
    /// </summary>
    /// <remarks>
    /// A request creating an organization names none, so its own calls about the new one (recording
    /// what the plan entitles it to, making its first collection) have no organization to carry. Only
    /// the organization is stated; the person stays the session's. Honoured in a product service only
    /// on an operation declared to create an organization, and only for one organization per
    /// request (<see cref="Bitwarden.Server.Sdk.Context.IOrganizationCreation"/>); refused everywhere else.
    /// </remarks>
    /// <param name="organization">The organization just created.</param>
    /// <returns>Ends the statement, restoring whatever was stated before, when disposed.</returns>
    /// <exception cref="ArgumentException"><paramref name="organization"/> is empty.</exception>
    public static IDisposable AboutCreatedOrganization(Guid organization) =>
        Begin(null, Check(organization, nameof(organization)), created: true);

    /// <summary>
    /// Makes the next outgoing call the service's own, about <paramref name="organization"/>,
    /// with nobody acting: no user and no provider is sent, whatever the request names.
    /// </summary>
    /// <remarks>
    /// For what a service does as the system in the middle of somebody's request: after an
    /// emergency-access takeover, removing the grantor from the organizations they don't own, with
    /// no one removing them. Under the grantee's session the callee would otherwise ask whether the
    /// grantee may remove members. It grants
    /// nothing a domain service lacks: its background work already calls with nobody acting.
    /// Refused in a product service, as every statement.
    /// </remarks>
    /// <example>
    /// <code>
    /// using (OutgoingCall.AsSystem(organization))
    /// {
    ///     await organizations.RemoveMembershipAsync(membership.Id, cancellationToken);
    /// }
    /// </code>
    /// </example>
    /// <param name="organization">The organization the call is about.</param>
    /// <returns>Ends the statement, restoring whatever was stated before, when disposed.</returns>
    /// <exception cref="ArgumentException"><paramref name="organization"/> is empty.</exception>
    public static IDisposable AsSystem(Guid organization) =>
        Begin(null, Check(organization, nameof(organization)), system: true);

    /// <summary>
    /// Makes the next outgoing call the service's own, about nobody: no user, no organization and no
    /// provider is sent, whatever the request names.
    /// </summary>
    /// <remarks>
    /// For an operation that names what it is about in its path or body and refuses a caller that
    /// also names an owner (<c>Organization = Forbidden, User = Forbidden</c>), asked in the middle of
    /// somebody's request: a service sending an invitation for an administrator asks whether the
    /// organization's single sign-on is enabled. The callee answers the service, not the
    /// administrator, and would refuse the administrator's organization. The credential travels as
    /// <c>SYSTEM</c>. Refused in a product service, as every statement.
    /// </remarks>
    /// <returns>Ends the statement, restoring whatever was stated before, when disposed.</returns>
    public static IDisposable AsSystem() =>
        Begin(null, null, system: true);

    /// <summary>
    /// Makes the next outgoing call the service's own, about <paramref name="organization"/>, with
    /// nobody acting, recording <paramref name="recordedAs"/> as the person who acted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// As <see cref="AsSystem(Guid)"/>: no user and no provider is sent, so the callee asks no actor
    /// rule of the call. The person travels only as the call's <c>recordedUser</c>
    /// (<c>Bitwarden-Context</c>), for the audit record, where the record names a person although the
    /// call is the system's: the policies a service turns on as the person saving a setting, or the
    /// memberships an emergency-access takeover removes.
    /// </para>
    /// <para>
    /// Audit-only at the callee: no authorization, row filter or cache reads it.
    /// </para>
    /// </remarks>
    /// <param name="organization">The organization the call is about.</param>
    /// <param name="recordedAs">The person the record names as having acted.</param>
    /// <returns>Ends the statement, restoring whatever was stated before, when disposed.</returns>
    /// <exception cref="ArgumentException">Either is empty.</exception>
    public static IDisposable AsSystem(Guid organization, Guid recordedAs) =>
        Begin(
            null,
            Check(organization, nameof(organization)),
            system: true,
            recordedUser: Check(recordedAs, nameof(recordedAs)));

    /// <summary>
    /// Makes the next outgoing call the service's own, about one person: <paramref name="user"/> is
    /// named, and no organization and no provider is sent, whatever the request names.
    /// </summary>
    /// <remarks>
    /// For what a service does as the system about one account, across every organization it
    /// belongs to, wherever the request came from: when a recovery code turns two-step login off at
    /// sign-in, revoking the person wherever two-step login is required of them. Nothing the request
    /// carried may narrow that to one organization or refuse it. The credential travels as
    /// <c>SYSTEM</c>. Refused in a product service, as every statement.
    /// </remarks>
    /// <param name="user">The person the call is about.</param>
    /// <returns>Ends the statement, restoring whatever was stated before, when disposed.</returns>
    /// <exception cref="ArgumentException"><paramref name="user"/> is empty.</exception>
    public static IDisposable AsSystemAbout(Guid user) =>
        Begin(Check(user, nameof(user)), null, system: true);

    /// <summary>
    /// Makes the next outgoing call the service's own, about one provider: <paramref name="provider"/>
    /// is named, and no user and no organization is sent, whatever the request names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For what a service does as the system about a provider, in the middle of a request that names
    /// none: recording a provider's invoice lines when a payment processor's webhook arrives, it asks
    /// for the provider's client organizations. The webhook names nobody, and the provider is the one
    /// the invoice's subscription belongs to. The credential travels as <c>SYSTEM</c>, so the callee applies the rules
    /// it keeps for a call nobody is making for anyone; an operation that doesn't accept a provider
    /// refuses it as it refuses any caller naming one. Refused in a product service, as every statement.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using (OutgoingCall.AsSystemForProvider(provider))
    /// {
    ///     clients = await organizations.ListProviderOrganizationsAsync(1, 100, cancellationToken);
    /// }
    /// </code>
    /// </example>
    /// <param name="provider">The provider the call is about.</param>
    /// <returns>Ends the statement, restoring whatever was stated before, when disposed.</returns>
    /// <exception cref="ArgumentException"><paramref name="provider"/> is empty.</exception>
    public static IDisposable AsSystemForProvider(Guid provider) =>
        Begin(null, null, system: true, provider: Check(provider, nameof(provider)));

    /// <summary>
    /// Sends the next outgoing call without the provider the context names: the call is about the
    /// person, not the provider their session is for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one statement a product service may make freely, because it names nobody and only takes
    /// away. A product service listing the organizations the signed-in person owns, so they can add
    /// one to their provider, asks about the person across organizations, which the callee answers
    /// only with no provider in context: with one, the question would be about the provider's staff,
    /// not the person.
    /// </para>
    /// <para>
    /// Everything else (the person, the organization, where the request came from) is carried as it would have been.
    /// </para>
    /// </remarks>
    /// <returns>Ends the statement, restoring whatever was stated before, when disposed.</returns>
    public static IDisposable WithoutProvider() =>
        Begin(null, null, withoutProvider: true);

    /// <summary>
    /// States <paramref name="organization"/> as the organization of the next outgoing call, and sends
    /// it without the provider the context names: the call is about the person in that organization,
    /// not the provider their session is for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A service deciding what the acting person reaches in an organization asks for their
    /// membership there, which the callee answers only with no provider in context. The person's
    /// standing is the same whatever provider their session is for, and whether they are its staff is
    /// the callee's to say from its own records; so the provider is left behind.
    /// </para>
    /// <para>
    /// The person and where the request came from are carried as they would have been. Refused in a product service, as every statement naming an organization.
    /// </para>
    /// </remarks>
    /// <param name="organization">The organization the call is about.</param>
    /// <returns>Ends the statement, restoring whatever was stated before, when disposed.</returns>
    /// <exception cref="ArgumentException"><paramref name="organization"/> is empty.</exception>
    public static IDisposable AboutOrganizationWithoutProvider(Guid organization) =>
        Begin(null, Check(organization, nameof(organization)), withoutProvider: true);

    /// <summary>
    /// Takes what is stated for the call being sent, or null when nothing is.
    /// </summary>
    /// <exception cref="InvalidOperationException">The statement was already used by an earlier call.</exception>
    internal static Stated? Take()
    {
        if (Ambient.Value is not { } statement)
        {
            return null;
        }

        if (Interlocked.Exchange(ref statement.Used, 1) == 1)
        {
            throw new InvalidOperationException(
                $"{nameof(OutgoingCall)} states the context of one outgoing call, and a second call was made " +
                "inside the same statement. Wrap only the call it is about, and begin a statement for each call.");
        }

        return statement.Value;
    }

    /// <summary>
    /// What is stated for the next call, without using the statement up: a cached read looks
    /// first, and takes it only if it answers.
    /// </summary>
    internal static Stated? Peek() =>
        Ambient.Value is { Used: 0 } statement ? statement.Value : null;

    private static Guid Check(Guid value, string name) =>
        value == Guid.Empty
            ? throw new ArgumentException("An empty identifier names nothing.", name)
            : value;

    private static IDisposable Begin(
        Guid? user,
        Guid? organization,
        bool created = false,
        bool system = false,
        Guid? recordedUser = null,
        bool withoutProvider = false,
        Guid? provider = null)
    {
        var previous = Ambient.Value;
        Ambient.Value = new Statement(new Stated(user, organization, created, system, recordedUser, withoutProvider, provider));

        return new Restore(previous);
    }

    /// <summary>
    /// What one statement says about the call it covers.
    /// </summary>
    /// <param name="User">The person named, if any.</param>
    /// <param name="Organization">The organization named, if any.</param>
    /// <param name="Created">Whether the organization is the one the request just created.</param>
    /// <param name="System">Whether the call is the service's own: no provider, and the person only if named.</param>
    /// <param name="RecordedUser">On a system call, the person the record names as having acted.</param>
    /// <param name="WithoutProvider">Whether the call leaves the context's provider behind.</param>
    /// <param name="Provider">On a system call, the provider it is about (<see cref="AsSystemForProvider"/>).</param>
    internal readonly record struct Stated(
        Guid? User,
        Guid? Organization,
        bool Created,
        bool System,
        Guid? RecordedUser,
        bool WithoutProvider = false,
        Guid? Provider = null);

    private sealed class Statement(Stated value)
    {
        public readonly Stated Value = value;

        public int Used;
    }

    private sealed class Restore(Statement? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Ambient.Value = previous;
            _disposed = true;
        }
    }
}
