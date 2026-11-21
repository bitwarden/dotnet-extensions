using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Context;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Publishes each cacheable read in the contract, as <c>x-bitwarden-cache</c> on every operation whose handler takes
/// a cacheable query, for the clients generated from the contract.
/// </summary>
/// <remarks>
/// <code>
/// "x-bitwarden-cache": {
///   "resource": "group",
///   "key": "organization-service:group:{key}",
///   "keyedBy": "id",
///   "scope": "organizations.read",
///   "context": { "organization": "required", "user": "optional", "provider": "optional" },
///   "owner": { "axis": "organization", "self": false, "acrossOwners": false },
///   "assertedBy": [ "audit-service" ],
///   "fields": [ "createdAt", "externalId", "memberships", "name", "updatedAt", "version" ],
///   "perCaller": [ ],
///   "format": 1
/// }
/// </code>
/// <para>
/// <c>key</c> holds <c>{key}</c> for the identifier or key the request names, and <c>{organization}</c>,
/// <c>{user}</c> or <c>{provider}</c> for a party the call carries: the owner of a key within its owner
/// (<c>organization-service:membership:by-user:{organization}:{key}</c>), or the key of a read of <c>current</c>
/// (<c>…:by-user:{organization}:{user}</c>), which every call the operation admits names.
/// A client reads the key, checks the scope on its own credential and the request's context as
/// the operation would, checks the entry's owner against the request and its format against <c>format</c>, and answers
/// only a request asking for none of the <c>perCaller</c> attributes: <c>fields</c> are every other attribute, all of
/// which the entry holds, and a request sending no fieldset asks for every attribute. <c>owner</c> is absent for a row nobody owns; <c>self</c> is a row that is
/// its own owner, which a request may name or leave unnamed; <c>acrossOwners</c> is a resource the
/// service reads across owners, which a client answers on the scope alone, as the service does.
/// A client that doesn't know <c>acrossOwners</c> checks the owner, which is never looser.
/// A cached read-many (<see cref="CacheableReadMany{TEntity,TResource}"/>) adds <c>"readMany": { "atMost": 1000 }</c>:
/// its entry's <c>d</c> is the array of every resource of the party its key names (<c>keyedBy</c>, the party in
/// context), in its order, of which a client takes the page asked for. A client that doesn't know <c>readMany</c> reads
/// the entry as one resource, which an array never is: a miss.
/// <c>assertedBy</c> names the services whose own assertion of a context naming a person the operation admits
/// (<c>[AcceptsAssertedContext]</c>): a client asserting one, on its own authority rather than
/// continuing a person's request, reads the cache only if it is among them, and asks otherwise, so the operation refuses
/// it as before. A client finding no <c>assertedBy</c> treats it as empty, which is never looser.
/// </para>
/// </remarks>
/// <param name="queries">The cacheable queries, and the operations settled to read them.</param>
/// <param name="identity">The service's name, which prefixes the keys.</param>
internal sealed class CacheContractTransformer(CacheableQueries queries, IServiceIdentity identity)
    : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        // The handler behind the operation.
        if (Bitwarden.Server.Sdk.Resources.Endpoints.OperationMethods.Of(context.Description.ActionDescriptor) is not { } method)
        {
            return Task.CompletedTask;
        }

        if (queries.Operation(method) is not { } read)
        {
            return Task.CompletedTask;
        }

        var (resource, settled) = read;

        var cache = new JsonObject
        {
            ["resource"] = resource.ResourceType,
            ["key"] = resource.PublishedTemplate(identity.Name, settled.Current),
            ["keyedBy"] = resource.KeyName,
            ["scope"] = settled.Scope,
            ["context"] = new JsonObject
            {
                ["organization"] = Requirement(settled.Organization),
                ["user"] = Requirement(settled.User),
                ["provider"] = Requirement(settled.Provider),
            },
            ["assertedBy"] = new JsonArray([.. (settled.AssertedBy ?? []).Select(caller => (JsonNode)caller)]),
            ["fields"] = new JsonArray([.. resource.Fields.Select(field => (JsonNode)field)]),
            ["perCaller"] = new JsonArray([.. resource.PerCaller.Order(StringComparer.Ordinal).Select(field => (JsonNode)field)]),
            ["format"] = OwnedCacheEntry.CurrentFormat,
        };

        if (resource.Owner is { } owner)
        {
            cache["owner"] = new JsonObject
            {
                ["axis"] = CacheFill.Axis(owner),
                ["self"] = resource.Ownership == Data.ReadOwnership.Self,
                ["acrossOwners"] = resource.AcrossOwners,
            };
        }

        // A cached read-many: the whole of its party's rows in one entry, of which a client takes the page asked for.
        if (resource.ReadMany is { } many)
        {
            cache["readMany"] = new JsonObject { ["atMost"] = many.AtMost };
        }

        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions[OwnedCacheEntry.Extension] = new JsonNodeExtension(cache);

        return Task.CompletedTask;
    }

    private static string Requirement(ContextRequirement requirement) => requirement switch
    {
        ContextRequirement.Required => "required",
        ContextRequirement.Forbidden => "forbidden",
        _ => "optional",
    };
}
