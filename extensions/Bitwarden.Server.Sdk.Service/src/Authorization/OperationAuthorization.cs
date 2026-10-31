using Microsoft.AspNetCore.Authorization;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Settles what one operation demands, and checks the declarations that depend on it.
/// </summary>
/// <remarks>
/// <para>
/// Each function works on an operation's endpoint metadata, which the endpoint conventions pass
/// from the endpoint builder.
/// </para>
/// <para>
/// The service, then the handler's class, then the operation, each
/// overriding the last. Resolved here rather than left to the framework because authorization
/// requirements compose as <em>and</em>: two declared requirements would both have to pass, so
/// an operation could tighten its class but never relax it. One attribute reaches the
/// pipeline, and it is the settled one.
/// </para>
/// </remarks>
internal static class OperationAuthorization
{
    /// <summary>
    /// Settles one operation's requirement from the three places it can be said.
    /// </summary>
    /// <param name="declared">The operation's own attribute.</param>
    /// <param name="inherited">The attribute on the class declaring the handler.</param>
    /// <param name="defaults">The service's.</param>
    /// <param name="reads">Whether the operation only reads, which picks the default scope.</param>
    /// <returns>The settled attribute, the one requirement the pipeline sees.</returns>
    /// <remarks>
    /// A role, a permission and <c>Anonymous</c> are read from the operation only. The first two
    /// are about what one operation demands of a caller, and the third turns authorization off:
    /// inherited, it would do so for every operation beneath it, which is not a thing anybody
    /// should be able to write in one line at the top of a file.
    /// </remarks>
    public static AuthorizationAttribute Settle(
        AuthorizationAttribute? declared,
        AuthorizationAttribute? inherited,
        AuthorizationDefaults defaults,
        bool reads) =>
        new()
        {
            Settled = true,
            Organization = Resolve(declared?.Organization, inherited?.Organization, defaults.Organization),
            User = Resolve(declared?.User, inherited?.User, defaults.User),
            Provider = Resolve(declared?.Provider, inherited?.Provider, defaults.Provider),
            Scope = declared?.Scope is { Length: > 0 } named
                ? named
                : inherited?.Scope is { Length: > 0 } shared
                    ? shared
                    : (reads ? defaults.ReadScope : defaults.WriteScope) ?? string.Empty,

            // The operation's own, never its class's.
            OrganizationRole = declared?.OrganizationRole ?? OrganizationRole.None,
            Permission = declared?.Permission ?? OrganizationPermission.None,
            ProviderRole = declared?.ProviderRole ?? ProviderRole.None,
            Anonymous = declared?.Anonymous ?? false,
        };

    /// <summary>
    /// Puts a settled requirement on an operation's metadata.
    /// </summary>
    /// <remarks>
    /// Added rather than replacing anything: the declared attributes stay where an author wrote
    /// them and simply demand nothing, so this is the one requirement the pipeline sees.
    /// <para>
    /// The handler honours <c>Anonymous</c>, but it never gets the chance: the authorization
    /// middleware challenges an unauthenticated caller before any requirement runs unless the
    /// endpoint says it allows anonymous access. Saying it here is what makes the attribute's
    /// flag mean anything.
    /// </para>
    /// </remarks>
    public static void Apply(IList<object> metadata, AuthorizationAttribute settled)
    {
        metadata.Add(settled);

        if (settled.Anonymous)
        {
            metadata.Add(new AllowAnonymousAttribute());
        }
    }

    /// <summary>
    /// The settled requirements on an operation's metadata: one, once it is settled.
    /// </summary>
    public static List<AuthorizationAttribute> SettledIn(IEnumerable<object> metadata) =>
        metadata.OfType<AuthorizationAttribute>().Where(attribute => attribute.GetRequirements().Any()).ToList();

    /// <summary>
    /// Replaces the single-scope requirement on an operation marked <see cref="AnyScopeAttribute"/>
    /// with one admitting any of its scopes.
    /// </summary>
    /// <param name="name">The operation, for the message that refuses it.</param>
    /// <param name="anyScope">Its declaration.</param>
    /// <param name="metadata">Its endpoint metadata, already settled.</param>
    /// <exception cref="InvalidOperationException">The declaration cannot be honoured.</exception>
    public static void ApplyAnyScope(string name, AnyScopeAttribute anyScope, IList<object> metadata)
    {
        if (anyScope.Scopes.Count == 0)
        {
            throw new InvalidOperationException($"'{name}' is marked [AnyScope] but names no scope.");
        }

        var settled = SettledIn(metadata);

        if (settled.Count != 1)
        {
            throw new InvalidOperationException(
                $"'{name}' is marked [AnyScope] but has no settled authorization to replace.");
        }

        var requirement = settled[0];

        if (requirement.Organization != ContextRequirement.Optional
            || requirement.User != ContextRequirement.Optional
            || requirement.Provider is not (ContextRequirement.Optional or ContextRequirement.Forbidden)
            || requirement.OrganizationRole != OrganizationRole.None
            || requirement.Permission != OrganizationPermission.None
            || requirement.ProviderRole != ProviderRole.None
            || requirement.Anonymous)
        {
            throw new InvalidOperationException(
                $"'{name}' is marked [AnyScope], which checks scopes only. Declare its "
                + "organization and user optional, its provider optional or forbidden, and check the rest in the operation.");
        }

        metadata.Remove(requirement);
        metadata.Add(new AnyScopeRequirement(
            anyScope.Scopes, ForbidsProvider: requirement.Provider == ContextRequirement.Forbidden));
    }

    /// <summary>
    /// Refuses an operation declaring <see cref="NamesOrganizationAttribute"/> or
    /// <see cref="NamesProviderAttribute"/> in a product service that says nowhere where it names
    /// it, lets the session name one too, names no person, or demands a role or a permission.
    /// </summary>
    /// <param name="name">The operation, for the message that refuses it.</param>
    /// <param name="organization">Whether it names an organization.</param>
    /// <param name="provider">Whether it names a provider.</param>
    /// <param name="parameters">Its parameters, by name and type.</param>
    /// <param name="metadata">Its endpoint metadata, already settled.</param>
    /// <exception cref="InvalidOperationException">The declaration cannot be honoured.</exception>
    public static void CheckNamesOwner(
        string name,
        bool organization,
        bool provider,
        IEnumerable<(string Name, Type Type)> parameters,
        IList<object> metadata)
    {
        var declared = parameters.ToList();

        if (organization && !Names(declared, NamesOrganizationAttribute.RouteValue, typeof(INamesOrganization)))
        {
            throw new InvalidOperationException(
                $"'{name}' names an organization in its request, so its path must carry "
                + $"{{{NamesOrganizationAttribute.RouteValue}}} or its body implement {nameof(INamesOrganization)}.");
        }

        if (provider && !Names(declared, NamesProviderAttribute.RouteValue, typeof(INamesProvider)))
        {
            throw new InvalidOperationException(
                $"'{name}' names a provider in its request, so its path must carry "
                + $"{{{NamesProviderAttribute.RouteValue}}} or its body implement {nameof(INamesProvider)}.");
        }

        var settled = SettledIn(metadata);

        if (settled.Count != 1
            || settled[0].Anonymous
            || settled[0].User != ContextRequirement.Required
            || (organization && settled[0].Organization != ContextRequirement.Forbidden)
            || (provider && settled[0].Provider != ContextRequirement.Forbidden))
        {
            throw new InvalidOperationException(
                $"'{name}' names an organization or provider in its request, so it must require a person "
                + "and forbid the session from naming one too: the one it names is the one it means.");
        }

        if (settled[0].OrganizationRole != OrganizationRole.None
            || settled[0].Permission != OrganizationPermission.None
            || settled[0].ProviderRole != ProviderRole.None)
        {
            throw new InvalidOperationException(
                $"'{name}' names an organization or provider its session was not issued for, so it may "
                + "demand no role or permission: the caller holds none there.");
        }

        if (metadata.OfType<AcceptsOrganizationCredentialAttribute>().Any()
            || metadata.OfType<AcceptsSendCredentialAttribute>().Any()
            || metadata.OfType<CreatesOrganizationAttribute>().Any())
        {
            throw new InvalidOperationException(
                $"'{name}' names an organization or provider in its request; it can't also accept an "
                + "organization's or a Send's credential, or create an organization.");
        }
    }

    /// <summary>
    /// Refuses an operation that accepts a Send credential without demanding a scope and
    /// forbidding a user and an organization, or that also accepts an organization credential.
    /// </summary>
    /// <param name="name">The operation, for the message that refuses it.</param>
    /// <param name="metadata">Its endpoint metadata, already settled.</param>
    /// <exception cref="InvalidOperationException">The declaration cannot be honoured.</exception>
    public static void CheckAcceptsSendCredential(string name, IList<object> metadata)
    {
        var settled = SettledIn(metadata);

        if (settled.Count != 1
            || settled[0] is not { Scope.Length: > 0, User: ContextRequirement.Forbidden, Organization: ContextRequirement.Forbidden }
            || settled[0].Anonymous)
        {
            throw new InvalidOperationException(
                $"'{name}' accepts a Send credential, so it must demand a scope and forbid both a user "
                + "and an organization: the credential names a Send and nobody.");
        }

        if (metadata.OfType<AcceptsOrganizationCredentialAttribute>().Any()
            || metadata.OfType<NamesOrganizationAttribute>().Any()
            || metadata.OfType<NamesProviderAttribute>().Any())
        {
            throw new InvalidOperationException(
                $"'{name}' accepts a Send credential; it can't also accept an organization's.");
        }
    }

    /// <summary>
    /// Refuses an operation declaring <see cref="CreatesOrganizationAttribute"/> that doesn't
    /// forbid an organization and require a person.
    /// </summary>
    /// <param name="name">The operation, for the message that refuses it.</param>
    /// <param name="metadata">Its endpoint metadata, already settled.</param>
    /// <exception cref="InvalidOperationException">The declaration cannot be honoured.</exception>
    public static void CheckCreatesOrganization(string name, IList<object> metadata)
    {
        var settled = SettledIn(metadata);

        if (settled.Count != 1
            || settled[0] is not { Organization: ContextRequirement.Forbidden, User: ContextRequirement.Required }
            || settled[0].Anonymous)
        {
            throw new InvalidOperationException(
                $"'{name}' creates an organization, so it must forbid naming one and require a person: "
                + "the organization it may name is the one it creates.");
        }
    }

    /// <remarks>
    /// <see cref="ContextRequirement.Unset"/> is zero, so a level that never mentioned a
    /// requirement is distinguishable from one that chose the platform's default, which is
    /// what makes inheriting possible at all. A nullable enumeration would have said the same
    /// thing more plainly and is not a legal attribute argument.
    /// </remarks>
    private static ContextRequirement Resolve(
        ContextRequirement? declared,
        ContextRequirement? inherited,
        ContextRequirement fallback) =>
        declared is { } named and not ContextRequirement.Unset
            ? named
            : inherited is { } shared and not ContextRequirement.Unset
                ? shared
                : fallback;

    private static bool Names(IEnumerable<(string Name, Type Type)> parameters, string routeValue, Type body) =>
        parameters.Any(parameter =>
            string.Equals(parameter.Name, routeValue, StringComparison.Ordinal)
            || body.IsAssignableFrom(parameter.Type));
}
