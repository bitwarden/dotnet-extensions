using System.Reflection;
using System.Text.Json.Nodes;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// One indexed question, as the owner's contract publishes it.
/// </summary>
/// <param name="Name">The question's name, which its switch is under.</param>
/// <param name="Resource">The resource type of its answer, cached by identifier.</param>
/// <param name="Key">The index key template, with <c>{subject}</c>, <c>{generation}</c> and, for a question asked inside an owner, <c>{owner}</c> (the owner <paramref name="Within"/> names; empty without one).</param>
/// <param name="Generation">The generation key template, with <c>{subject}</c>.</param>
/// <param name="SubjectAxis">Which of the call's context names the principal; null for a subject the caller names.</param>
/// <param name="SubjectAttribute">The resource attribute holding the subject, re-checked on every row; null where it is not held.</param>
/// <param name="Within">The owner the question is asked inside, which the call must name; null across owners.</param>
/// <param name="Carries">Attributes the answer carries for all its resources, which their own entries do not hold.</param>
/// <param name="Where">What else the question asks of each resource: attribute, accepted values.</param>
/// <param name="Scope">The scope the question demands.</param>
/// <param name="Organization">Whether a call must, may or must not name an organization.</param>
/// <param name="User">Likewise, an acting user.</param>
/// <param name="Provider">Likewise, a provider.</param>
public sealed record CacheableIndexRead(
    string Name,
    string Resource,
    string Key,
    string Generation,
    string? SubjectAxis,
    string? SubjectAttribute,
    string? Within,
    IReadOnlySet<string> Carries,
    IReadOnlyDictionary<string, IReadOnlySet<string>> Where,
    string Scope,
    string Organization,
    string User,
    string Provider)
{
    /// <summary>
    /// The extension publishing an index.
    /// </summary>
    public const string Extension = "x-bitwarden-index";

    /// <summary>
    /// Every indexed question a contract publishes, by the list operation answering it and the question's name.
    /// </summary>
    /// <param name="contract">The owner's OpenAPI document.</param>
    /// <returns>The questions, by <c>{operationId}/{name}</c>.</returns>
    public static IReadOnlyDictionary<string, CacheableIndexRead> FromContract(Stream contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var indexes = new Dictionary<string, CacheableIndexRead>(StringComparer.Ordinal);
        var document = JsonNode.Parse(contract);

        foreach (var (_, path) in document?["paths"]?.AsObject() ?? [])
        {
            foreach (var (_, operation) in path?.AsObject() ?? [])
            {
                if (operation?["operationId"]?.GetValue<string>() is not { } id)
                {
                    continue;
                }

                foreach (var index in operation[Extension] switch
                {
                    JsonArray several => several.OfType<JsonObject>(),
                    JsonObject one => [one],
                    _ => [],
                })
                {

                    var context = index["context"];
                    var where = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

                    foreach (var (attribute, values) in index["where"]?.AsObject() ?? [])
                    {
                        where[attribute] = (values?.AsArray() ?? []).Select(value => value!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
                    }

                    indexes[$"{id}/{index["name"]!.GetValue<string>()}"] = new CacheableIndexRead(
                        index["name"]!.GetValue<string>(),
                        index["resource"]!.GetValue<string>(),
                        index["key"]!.GetValue<string>(),
                        index["generation"]!.GetValue<string>(),
                        index["subject"]?["axis"]?.GetValue<string>(),
                        index["subject"]?["attribute"]?.GetValue<string>(),
                        index["within"]?.GetValue<string>(),
                        (index["carries"]?.AsArray() ?? []).Select(attribute => attribute!.GetValue<string>()).ToHashSet(StringComparer.Ordinal),
                        where,
                        index["scope"]!.GetValue<string>(),
                        context?["organization"]?.GetValue<string>() ?? "optional",
                        context?["user"]?.GetValue<string>() ?? "optional",
                        context?["provider"]?.GetValue<string>() ?? "optional");
                }
            }
        }

        return indexes;
    }

    /// <summary>
    /// The indexed questions in the contract a client assembly embeds as <c>openapi.json</c>.
    /// </summary>
    /// <param name="client">The client package's assembly.</param>
    /// <returns>The questions; none when the assembly embeds no contract.</returns>
    public static IReadOnlyDictionary<string, CacheableIndexRead> FromAssembly(Assembly client)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var contract = client.GetManifestResourceStream("openapi.json");

        return contract is null ? new Dictionary<string, CacheableIndexRead>() : FromContract(contract);
    }
}
