using Bitwarden.Server.Sdk.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// The organization, acting user and provider an outgoing call carries: what an
/// <see cref="OutgoingCall"/> statement names, and otherwise the context's.
/// </summary>
/// <remarks>
/// One definition for the call that goes over HTTP and the read a client answers from the
/// service-owned cache, so the cache checks a row's owner against exactly what the service would
/// have been told.
/// </remarks>
internal static class OutgoingContext
{
    /// <summary>
    /// What the call carries.
    /// </summary>
    /// <param name="services">A scope's services, for the context and the service's identity.</param>
    /// <param name="stated">What an <see cref="OutgoingCall"/> statement names, if any.</param>
    /// <exception cref="InvalidOperationException">A product service names someone its session does not.</exception>
    public static (Guid? Organization, Guid? User, Guid? Provider) Of(
        IServiceProvider services,
        OutgoingCall.Stated? stated)
    {
        var context = services.GetService<IContext>();

        if (stated is not null && services.GetService<IServiceIdentity>() is { IsProductService: true } self
            && !(stated.Value is { Created: true, Organization: { } created }
                && services.GetService<IOrganizationCreation>()?.Allows(created) == true)
            && stated.Value is not { WithoutProvider: true, User: null, Organization: null, System: false })
        {
            throw new InvalidOperationException(
                $"{self.Name} is a product service: its calls are about the person and organization its session " +
                $"names, and {nameof(OutgoingCall)} may not name others, but for the organization an operation " +
                $"declared to create one has just created, or leave its provider behind ({nameof(OutgoingCall.WithoutProvider)}).");
        }

        // While an event is handled, its actor is for the record only: refused with OutgoingCall:RefuseEventActor
        // (as a test host sets it), logged otherwise.
        if (EventActor.Misuse(stated) is { } misuse)
        {
            if (services.GetService<IOptionsMonitor<OutgoingCallOptions>>()?.CurrentValue.RefuseEventActor == true)
            {
                throw new InvalidOperationException($"{misuse} OutgoingCall:RefuseEventActor is on.");
            }

            services.GetService<ILoggerFactory>()?.CreateLogger(typeof(OutgoingCall)).LogWarning(
                "Calling another service on an event's actor: {Misuse}",
                misuse);
        }

        // A system call carries a person only when it is about one, and a provider only when it is
        // about one (OutgoingCall.AsSystem, OutgoingCall.AsSystemAbout, OutgoingCall.AsSystemForProvider).
        if (stated is { System: true } system)
        {
            return (system.Organization, system.User, system.Provider);
        }

        // An operation that names an organization or provider in its path or body, where the
        // session names none, carries that one (IRequestNamedOwner); never in place of the
        // session's, and never in place of a statement's.
        var named = context?.OrganizationId is null || context.ProviderId is null
            ? services.GetService<IRequestNamedOwner>()
            : null;

        return (
            stated?.Organization ?? context?.OrganizationId ?? named?.OrganizationId,
            stated?.User ?? context?.UserId,
            stated is { WithoutProvider: true } ? null : context?.ProviderId ?? named?.ProviderId);
    }

    /// <summary>
    /// What the call carries in <see cref="BitwardenHeaders.Context"/>: where the originating request
    /// came from, as the context holds it, and how it was made.
    /// </summary>
    /// <param name="services">A scope's services, for the context.</param>
    /// <param name="stated">What an <see cref="OutgoingCall"/> statement names, if any.</param>
    /// <remarks>
    /// Carried unchanged from whoever set them first. This service's own view of the connection
    /// would show the peer that called it, which is the one thing these must never be replaced
    /// with. A system call's credential is <see cref="CallerCredential.System"/>, and only such a
    /// call carries a recorded person; the provider a staff member acts through is carried on every
    /// call their request makes, the system's own included. The party feature flags are evaluated
    /// for, and the client's version, are carried on every call, the system's own included: like the
    /// device, they say where the originating request came from, and every flag the request leads to
    /// is evaluated for the same party, as it would be in one server. The flag subject's
    /// organizations are carried only on a call that keeps the actor: never on a system call, nor on
    /// one an <see cref="OutgoingCall"/> statement makes about somebody else. A value of a shape the
    /// callee would refuse is left out rather than sent, so a bad value recorded upstream costs a
    /// member, never the call.
    /// </remarks>
    public static ContextValue Carried(IServiceProvider services, OutgoingCall.Stated? stated)
    {
        var context = services.GetService<IContext>();

        if (stated is { System: true } system)
        {
            return Where(context) with
            {
                Credential = CallerCredential.System,
                RecordedUser = system.RecordedUser,
            };
        }

        return Where(context) with
        {
            Credential = context?.Credential,
            RecordedUser = context?.Credential == CallerCredential.System ? context.RecordedUserId : null,
            Organizations = stated?.User is not { } named || named == context?.UserId
                ? MemberOrganizations(context?.UserOrganizationIds)
                : null,
        };
    }

    /// <summary>
    /// A flag subject's organizations worth carrying: each once, none empty, and no more than the callee
    /// accepts. A longer list is left out whole rather than cut to an arbitrary part.
    /// </summary>
    private static Guid[]? MemberOrganizations(IReadOnlyList<Guid>? organizations)
    {
        if (organizations is not { Count: > 0 })
        {
            return null;
        }

        Guid[] kept = [.. organizations.Where(organization => organization != Guid.Empty).Distinct()];
        return kept.Length is > 0 and <= ContextHeader.MaximumOrganizations ? kept : null;
    }

    private static ContextValue Where(IContext? context) => new()
    {
        ClientIp = Kept(context?.ClientIpAddress, ContextHeader.IsAddress),
        DeviceType = Kept(context?.DeviceType, ContextHeader.IsDeviceType),
        DeviceIdentifier = Kept(context?.DeviceIdentifier, ContextHeader.IsDeviceIdentifier),
        RecordedProvider = context?.RecordedProviderId is { } provider && provider != Guid.Empty ? provider : null,
        FlagKind = context is not null && Enum.IsDefined(context.FlagSubjectKind) ? context.FlagSubjectKind : FlagSubjectKind.User,
        FlagKey = context is not null && Enum.IsDefined(context.FlagSubjectKind) && context.FlagSubjectKey is { } key && key != Guid.Empty
            ? key
            : null,
        ClientVersion = Kept(context?.ClientVersion, ContextHeader.IsClientVersion),
        ClientVersionIsPrerelease = context?.ClientVersionIsPrerelease == true
            && ContextHeader.IsClientVersion(context.ClientVersion),
    };

    /// <summary>
    /// A value worth carrying: present, and of the shape the callee accepts.
    /// </summary>
    private static string? Kept(string? value, Func<string, bool> accepted) =>
        value is { Length: > 0 } && accepted(value) ? value : null;
}
