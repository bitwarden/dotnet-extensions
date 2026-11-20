using System.Reflection;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Resources.Caching;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// One cacheable read, as the owner's contract publishes it.
/// </summary>
/// <param name="Resource">The resource's type.</param>
/// <param name="Key">The key template, with <c>{key}</c> for the identifier or key.</param>
/// <param name="Scope">The scope the operation demands.</param>
/// <param name="Organization">Whether a call must, may or must not name an organization.</param>
/// <param name="User">Likewise, an acting user.</param>
/// <param name="Provider">Likewise, a provider.</param>
/// <param name="OwnerAxis">What owns the rows: <c>organization</c>, <c>user</c>, <c>provider</c>, or null.</param>
/// <param name="OwnerIsSelf">Whether the row is its own owner, which a call may leave unnamed.</param>
/// <param name="Fields">The attributes the cache holds: every attribute but <paramref name="PerCaller"/>.</param>
/// <param name="AcrossOwners">
/// Whether the service reads it across owners, answering any holder of the scope whatever owner the
/// call names; a cached read then checks the scope alone, as the service does. False when the
/// contract doesn't say, which keeps the owner check.
/// </param>
/// <param name="PerCaller">
/// The attributes the service works out per caller and never caches: a read asking for any of them, or sending no
/// fieldset when there are any, is answered by the service.
/// </param>
/// <param name="Format">
/// The entry format the contract's owner writes (<see cref="OwnedCacheEntry.CurrentFormat"/>); 0 when the contract
/// doesn't say, which no client reads.
/// </param>
/// <param name="AssertedBy">
/// The services whose own assertion of a context naming a person the operation admits (<c>assertedBy</c>); none when
/// the contract doesn't say, so a call asserting one is asked of the service.
/// </param>
public sealed record CacheableRead(
    string Resource,
    string Key,
    string Scope,
    string Organization,
    string User,
    string Provider,
    string? OwnerAxis,
    bool OwnerIsSelf,
    IReadOnlySet<string> Fields,
    bool AcrossOwners = false,
    IReadOnlySet<string>? PerCaller = null,
    int Format = 0,
    IReadOnlySet<string>? AssertedBy = null)
{
    /// <summary>
    /// Every cacheable read a contract publishes, by operation.
    /// </summary>
    /// <param name="contract">The owner's OpenAPI document.</param>
    /// <returns>The reads, by <c>operationId</c>.</returns>
    public static IReadOnlyDictionary<string, CacheableRead> FromContract(Stream contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var reads = new Dictionary<string, CacheableRead>(StringComparer.Ordinal);
        var document = JsonNode.Parse(contract);

        foreach (var (_, path) in document?["paths"]?.AsObject() ?? [])
        {
            foreach (var (_, operation) in path?.AsObject() ?? [])
            {
                if (operation?[OwnedCacheEntry.Extension] is not JsonObject cache
                    || operation["operationId"]?.GetValue<string>() is not { } id)
                {
                    continue;
                }

                var context = cache["context"];
                reads[id] = new CacheableRead(
                    cache["resource"]!.GetValue<string>(),
                    cache["key"]!.GetValue<string>(),
                    cache["scope"]!.GetValue<string>(),
                    context?["organization"]?.GetValue<string>() ?? "optional",
                    context?["user"]?.GetValue<string>() ?? "optional",
                    context?["provider"]?.GetValue<string>() ?? "optional",
                    cache["owner"]?["axis"]?.GetValue<string>(),
                    cache["owner"]?["self"]?.GetValue<bool>() ?? false,
                    (cache["fields"]?.AsArray() ?? []).Select(field => field!.GetValue<string>()).ToHashSet(StringComparer.Ordinal),
                    cache["owner"]?["acrossOwners"]?.GetValue<bool>() ?? false,
                    (cache["perCaller"]?.AsArray() ?? []).Select(field => field!.GetValue<string>()).ToHashSet(StringComparer.Ordinal),
                    cache["format"]?.GetValue<int>() ?? 0,
                    Names(cache["assertedBy"]));
            }
        }

        return reads;
    }

    /// <summary>
    /// A list of names a contract publishes; none when it publishes none.
    /// </summary>
    internal static IReadOnlySet<string> Names(JsonNode? names) =>
        (names as JsonArray ?? []).Select(name => name!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The cacheable reads in the contract a client assembly embeds as <c>openapi.json</c>.
    /// </summary>
    /// <param name="client">The client package's assembly.</param>
    /// <returns>The reads; none when the assembly embeds no contract.</returns>
    public static IReadOnlyDictionary<string, CacheableRead> FromAssembly(Assembly client)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var contract = client.GetManifestResourceStream("openapi.json");

        return contract is null ? new Dictionary<string, CacheableRead>() : FromContract(contract);
    }
}
