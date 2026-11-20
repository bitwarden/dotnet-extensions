using Bitwarden.Server.Sdk.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Carries the current organization, acting user, provider and originating client across the call.
/// </summary>
/// <remarks>
/// <para>
/// Done here rather than at each call site, because a call site that forgets produces a request
/// the callee handles happily and attributes to nobody. A missing acting user is not a failure a
/// test would catch; it is an audit record that quietly says less than it should.
/// </para>
/// <para>
/// The acting user and organization are the context's, except on a call made inside an
/// <see cref="OutgoingCall"/> statement, where what was stated replaces them. That
/// is the only thing here a call site can change, and only in a domain service.
/// </para>
/// <para>
/// Three plain headers say whom the call concerns (<see cref="BitwardenHeaders.Organization"/>,
/// <see cref="BitwardenHeaders.User"/>, and <see cref="BitwardenHeaders.Provider"/> only when the
/// context has one), and <see cref="BitwardenHeaders.Context"/> carries everything else. All four
/// are written here and nowhere else: whatever a call site put on the request
/// itself is removed first.
/// </para>
/// <para>
/// Trace context needs no such help: <c>HttpClient</c> emits <c>traceparent</c> from the ambient
/// <c>Activity</c> on its own, so W3C propagation is already in place.
/// </para>
/// <para>
/// Then it signs them: a context token Identity issues for this call's callee and this service
/// (<see cref="ContextToken"/>), in <see cref="BitwardenHeaders.Delegation"/>, which the callee
/// believes in place of the four. Exchanged from what proves whom the request is for
/// (<see cref="ICallerProof"/>) when the call carries the request's own context, so it stays chained
/// to the person; from this service's own token when an <see cref="OutgoingCall"/> statement names
/// somebody else or makes the call the system's, or when there is no request.
/// </para>
/// </remarks>
/// <param name="scopes">Supplies the scope the current operation is running in.</param>
/// <param name="tokens">This client's credential, which also obtains its context tokens.</param>
internal sealed class ContextHandler(IServiceScopeFactory scopes, IServiceTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Resolved per call from a fresh scope, never injected: IHttpClientFactory pools handlers
        // for the lifetime of a client, so a scoped dependency captured in the constructor would
        // answer for whichever request happened to create the handler.
        //
        // This is why IContext must read ambient state or be a singleton. The serving side
        // satisfies that: its implementation reads IHttpContextAccessor, which flows on the
        // execution context and so is the same request's context even in a scope created here.
        // An implementation holding state set in the caller's own scope would not be seen.
        using var scope = scopes.CreateScope();

        // Taken before anything is written, so a misuse refuses the call rather than sending it
        // with half its headers.
        // Once per call, not per attempt: a retry sends the same call, and a statement covers exactly one.
        OutgoingCall.Stated? stated;
        if (request.Options.TryGetValue(Taken, out var held) && held is { } taken)
        {
            stated = taken.Value;
        }
        else
        {
            stated = OutgoingCall.Take();
            request.Options.Set(Taken, new TakenStatement(stated));
        }

        var (organization, user, provider) = OutgoingContext.Of(scope.ServiceProvider, stated);
        var carried = OutgoingContext.Carried(scope.ServiceProvider, stated);

        foreach (var header in BitwardenHeaders.All)
        {
            request.Headers.Remove(header);
        }

        if (organization is { } named)
        {
            request.Headers.TryAddWithoutValidation(BitwardenHeaders.Organization, named.ToString());
        }

        if (user is { } acting)
        {
            request.Headers.TryAddWithoutValidation(BitwardenHeaders.User, acting.ToString());
        }

        // Only when the context has one: an operation takes a provider only where it declares so.
        if (provider is { } through)
        {
            request.Headers.TryAddWithoutValidation(BitwardenHeaders.Provider, through.ToString());
        }

        // Always, if only to say which version it speaks.
        var written = ContextHeader.Write(carried);
        request.Headers.TryAddWithoutValidation(BitwardenHeaders.Context, written);

        // The services this request passed through, this one last, so a service called back while its own request
        // waits on this call can tell (OutgoingCallChain).
        OutgoingCallChain.Write(request, scope.ServiceProvider);

        var subject = SubjectToken(scope.ServiceProvider, stated);

        // The decision, never the token: the request memo keys a call by whether its context is delegated or asserted.
        request.Options.Set(Asserted, subject is null);

        ServiceFaultHandler.Obtaining(request, "a context token");
        var signed = await tokens.GetContextTokenAsync(
            new ContextAssertion(organization, user, provider, written),
            subject,
            cancellationToken);
        ServiceFaultHandler.Obtained(request);

        if (signed is { Length: > 0 })
        {
            request.Headers.TryAddWithoutValidation(BitwardenHeaders.Delegation, signed);
        }

        return await base.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// What the call's context token is exchanged from: the request's proof for a call carrying the request's own
    /// context, so it stays delegated, chained to the person; null where the call is this service's own word (a
    /// statement naming somebody else or making the call the system's, or no request at all), so the context is asserted
    /// on this service's own token. The one decision, for the call and for a read of the cache in its place.
    /// </summary>
    /// <param name="services">A scope's services, for the request's context and its proof.</param>
    /// <param name="stated">What an <see cref="OutgoingCall"/> statement names, if any.</param>
    internal static string? SubjectToken(IServiceProvider services, OutgoingCall.Stated? stated)
    {
        var requestContext = services.GetService<IContext>();

        return Continues(stated, requestContext?.UserId, requestContext?.OrganizationId)
            ? services.GetService<ICallerProof>()?.SubjectToken
            : null;
    }

    /// <summary>
    /// Whether the call's context is asserted on this service's own word (true) or delegated, chained to the person
    /// (false): written on every call, for the request memo's key (<see cref="RequestMemoHandler"/>).
    /// </summary>
    internal static readonly HttpRequestOptionsKey<bool> Asserted = new("Bitwarden.Client.Asserted");

    /// <summary>
    /// The statement the call's first attempt took, for its retries.
    /// </summary>
    private static readonly HttpRequestOptionsKey<TakenStatement?> Taken = new("Bitwarden.Client.Statement");

    private sealed record TakenStatement(OutgoingCall.Stated? Value);

    /// <summary>
    /// Whether a call carries the request's own person, and so stays chained to what proved them: no statement; the two
    /// statements a product service may make (the organization it just created, leaving its provider behind); or a
    /// statement naming the request's own person, an organization where the request names none, or the same one. A
    /// statement naming somebody else, or making the call the system's, or naming an organization other than the
    /// request's, is the service's own word.
    /// </summary>
    /// <param name="stated">The statement, if any.</param>
    /// <param name="requestUser">The person the request being handled names, if any.</param>
    /// <param name="requestOrganization">The organization the request being handled names, if any.</param>
    private static bool Continues(OutgoingCall.Stated? stated, Guid? requestUser, Guid? requestOrganization)
    {
        if (stated is not { } said)
        {
            return true;
        }

        // A system call, or one naming somebody other than the request's person, is the service's own word.
        if (said.System || (said.User is { } named && named != requestUser))
        {
            return false;
        }

        // An organization: the one just created, one where the request names none, or the request's own.
        return said.Organization is null
            || said.Created
            || requestOrganization is null
            || said.Organization == requestOrganization;
    }
}
