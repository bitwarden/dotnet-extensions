using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Context;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Finds the operations that read a cacheable query, and refuses to start when a cached copy could not be authorized
/// as the service authorizes the read.
/// </summary>
/// <remarks>
/// <para>
/// A client may answer from the cache only what the service would answer the same caller, and it
/// can repeat two of the service's checks: the scope on its own credential, and the request's
/// context against the row's owner.
/// So the operation may demand those and nothing else: no role, no permission, no requirement or policy of its own, no
/// other credential, not anonymous, not one of several scopes. Nothing may stand between the request and the read
/// either: no endpoint filter, no check of the handler's own (it takes the identifier or key, the fieldset, a
/// cancellation token and the query, and nothing it could decide by). And the owner of a cache is a domain service; a
/// product service's reads are about a person's session.
/// </para>
/// <para>
/// What the operation demands, as the platform settled it, is recorded on the declaration and
/// published from there, so the client's check is the service's, not a copy of it.
/// </para>
/// <para>
/// Settled once every endpoint is built (<see cref="CacheContractCheck"/>), over every handler a
/// group's <c>WithStandards()</c> maps.
/// </para>
/// </remarks>
internal static class CacheContract
{
    /// <summary>
    /// Settles each cacheable read's operation, refusing what a client could not repeat.
    /// </summary>
    /// <param name="operations">Every operation the service serves.</param>
    /// <param name="queries">The cacheable queries, where each settled operation is recorded.</param>
    /// <param name="identity">Whether this is a domain service.</param>
    /// <param name="refuse">Told each problem.</param>
    public static void Settle(
        IReadOnlyList<CacheOperation> operations,
        CacheableQueries queries,
        IServiceIdentity identity,
        Action<string> refuse)
    {
        if (queries.Resources.Count == 0)
        {
            return;
        }

        if (identity.IsProductService)
        {
            refuse(
                $"{identity.Name} is a product service and declares cacheable resources. Only a domain "
                + "service owns a cache: a product service's reads are shaped for a person's session.");
            return;
        }

        foreach (var operation in operations)
        {
            var read = operation.Method.GetParameters()
                .Select(parameter => queries.ReadBy(parameter.ParameterType))
                .OfType<ICacheableResource>()
                .ToList();

            if (read.Count == 0)
            {
                continue;
            }

            if (read.Count > 1)
            {
                refuse($"{operation.Name} takes {read.Count} cacheable queries. A cacheable read takes one.");
                continue;
            }

            if (Settle(operation, read[0], queries, refuse) is { } settled)
            {
                queries.Settle(operation.Method, read[0], settled);
            }
        }
    }

    /// <summary>
    /// The services whose own assertion of a context naming a person the operation admits: a client of the cache
    /// asserting one on its own authority is answered only if it is among them, as the operation would answer it.
    /// </summary>
    private static IReadOnlyList<string> AssertedBy(CacheOperation operation) =>
        [.. (operation.Attribute<AcceptsAssertedContextAttribute>()?.Callers ?? []).Order(StringComparer.Ordinal)];

    private static Type? Document(Type returnType) =>
        returnType.IsGenericType
        && (returnType.GetGenericTypeDefinition() == typeof(Task<>) || returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            ? returnType.GetGenericArguments()[0]
            : returnType;

    private static CacheableOperation? Settle(
        CacheOperation operation,
        ICacheableResource resource,
        CacheableQueries queries,
        Action<string> refuse)
    {
        if (operation.Settled is not { } settled)
        {
            refuse($"{operation.Name} has no settled authorization to publish for its cache.");
            return null;
        }

        var (refusal, current) = Shape(operation, resource, queries);
        refusal ??= Demands(operation, settled) ?? Named(resource, settled, current);

        if (refusal is not null)
        {
            refuse(
                $"{operation.Name} reads {resource.Resource.Name} through its cacheable query, but {refusal}. A client "
                + "can repeat only a scope and the row's owner, so a cached copy would answer callers the service refuses.");
            return null;
        }

        return new CacheableOperation(
            settled.Scope!, settled.Organization, settled.User, settled.Provider, current, AssertedBy(operation));
    }

    /// <summary>
    /// The parties a client fills the key from must be in every call the operation admits: the owner of a key within
    /// its owner, and for a read of <c>current</c> the party it is read by.
    /// </summary>
    private static string? Named(ICacheableResource resource, AuthorizationAttribute settled, bool current)
    {
        static ContextRequirement Of(AuthorizationAttribute settled, Data.Owner axis) => axis switch
        {
            Data.Owner.Organization => settled.Organization,
            Data.Owner.User => settled.User,
            _ => settled.Provider,
        };

        if (resource.WithinOwner && resource.Owner is { } owner && Of(settled, owner) != ContextRequirement.Required)
        {
            return $"its key is within its {CacheFill.Axis(owner)}, which it doesn't require in context";
        }

        if (current && resource.CurrentFrom is { } axis && Of(settled, axis) != ContextRequirement.Required)
        {
            return $"it reads current by the {CacheFill.Axis(axis)} in context, which it doesn't require";
        }

        return null;
    }

    /// <summary>
    /// What the handler takes and returns: the identifier or key, the fieldset, a cancellation token and the query,
    /// and the resource's document. Anything else is something it could decide by that a client can't.
    /// </summary>
    private static (string? Refusal, bool Current) Shape(CacheOperation operation, ICacheableResource resource, CacheableQueries queries)
    {
        if (Document(operation.Method.ReturnType) != typeof(ResourceDocument<>).MakeGenericType(resource.Resource))
        {
            return ($"it doesn't return ResourceDocument<{resource.Resource.Name}>", false);
        }

        var fields = typeof(Querying.Fields<>).MakeGenericType(resource.Resource);
        var identified = false;

        foreach (var parameter in operation.Method.GetParameters())
        {
            var type = parameter.ParameterType;

            if (queries.ReadBy(type) is not null || type == fields || type == typeof(CancellationToken))
            {
                continue;
            }

            if (parameter.Name == resource.KeyName
                && (type == typeof(string) || (!resource.ById && (type == typeof(Guid) || type == typeof(Guid?)))))
            {
                identified = true;
                continue;
            }

            return ($"it takes '{parameter.Name}' besides the {resource.KeyName}, the fieldset, a cancellation token and the query", false);
        }

        // Taking no key, it reads current: the key is the party the context names, as the query declares.
        if (!identified && resource.CurrentFrom is not null)
        {
            return (null, true);
        }

        return (identified ? null : $"it doesn't take the {resource.KeyName} as '{resource.KeyName}'", false);
    }

    /// <summary>
    /// What the operation demands of a caller beyond a scope and the context a client repeats.
    /// </summary>
    private static string? Demands(CacheOperation operation, AuthorizationAttribute settled)
    {
        var metadata = operation.Metadata ?? [];
        var declaring = operation.Method.DeclaringType;

        return settled switch
        {
            { Anonymous: true } => "it is anonymous",
            { Scope: null or "" } => "it demands no scope",
            { OrganizationRole: not OrganizationRole.None } => $"it demands the {settled.OrganizationRole} role",
            { Permission: not OrganizationPermission.None } => $"it demands the {settled.Permission} permission",
            { ProviderRole: not ProviderRole.None } => $"it demands the provider {settled.ProviderRole} role",
            _ when operation.Attribute<AnyScopeAttribute>() is not null => "it accepts any of several scopes",
            _ when operation.Attribute<NamesOrganizationAttribute>() is not null
                || operation.Attribute<NamesProviderAttribute>() is not null => "it names an organization or provider its session does not",
            _ when operation.Attribute<AcceptsOrganizationCredentialAttribute>() is not null
                || declaring?.IsDefined(typeof(AcceptsOrganizationCredentialAttribute), inherit: true) == true
                || operation.Attribute<AcceptsSendCredentialAttribute>() is not null
                || declaring?.IsDefined(typeof(AcceptsSendCredentialAttribute), inherit: true) == true => "it accepts a credential that isn't a service's",
            _ when metadata.OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any(data => data is not AuthorizationAttribute
                    && (!string.IsNullOrEmpty(data.Policy) || !string.IsNullOrEmpty(data.Roles)))
                || metadata.OfType<Microsoft.AspNetCore.Authorization.IAuthorizationRequirementData>().Any(data => data is not AuthorizationAttribute)
                || metadata.OfType<Microsoft.AspNetCore.Authorization.AuthorizationPolicy>().Any() => "it demands a requirement or policy of its own",
            _ when operation.Method.GetCustomAttributes(inherit: true).OfType<IAuthorizesOperation>().Any()
                || (declaring?.GetCustomAttributes(inherit: true).OfType<IAuthorizesOperation>().Any() ?? false) => "it authorizes the caller itself (IAuthorizesOperation)",
            _ when operation.Filters > 0 => "it has endpoint filters, which run before the read and a client can't repeat",
            _ => null,
        };
    }
}
