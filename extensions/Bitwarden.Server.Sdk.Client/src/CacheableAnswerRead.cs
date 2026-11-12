using System.Reflection;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Resources.Caching;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// One answer an action gives, as the owner's contract publishes it.
/// </summary>
/// <param name="Name">The answer's name, which its switch is under.</param>
/// <param name="Key">The key template, with <c>{key}</c> for the subject.</param>
/// <param name="Digested">Whether the key is text, kept by its digest.</param>
/// <param name="Scope">The scope the action demands.</param>
/// <param name="Organization">Whether a call must, may or must not name an organization.</param>
/// <param name="User">Likewise, an acting user.</param>
/// <param name="Provider">Likewise, a provider.</param>
public sealed record CacheableAnswerRead(
    string Name,
    string Key,
    bool Digested,
    string Scope,
    string Organization,
    string User,
    string Provider)
{
    /// <summary>
    /// Every answer a contract publishes, by the action giving it and the answer's name.
    /// </summary>
    /// <param name="contract">The owner's OpenAPI document.</param>
    /// <returns>The answers, by <c>{operationId}/{name}</c>.</returns>
    public static IReadOnlyDictionary<string, CacheableAnswerRead> FromContract(Stream contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var answers = new Dictionary<string, CacheableAnswerRead>(StringComparer.Ordinal);
        var document = JsonNode.Parse(contract);

        foreach (var (_, path) in document?["paths"]?.AsObject() ?? [])
        {
            foreach (var (_, operation) in path?.AsObject() ?? [])
            {
                if (operation?[OwnedAnswerKey.Extension] is not JsonArray published
                    || operation["operationId"]?.GetValue<string>() is not { } id)
                {
                    continue;
                }

                foreach (var answer in published.OfType<JsonObject>())
                {
                    var context = answer["context"];
                    var name = answer["name"]!.GetValue<string>();
                    var digest = answer["digest"]?.GetValue<string>();

                    // A digest this client doesn't know can't be repeated: no subject it works out would be the owner's.
                    if (digest is not (null or OwnedAnswerKey.Digest))
                    {
                        continue;
                    }

                    answers[$"{id}/{name}"] = new CacheableAnswerRead(
                        name,
                        answer["key"]!.GetValue<string>(),
                        digest is not null,
                        answer["scope"]!.GetValue<string>(),
                        context?["organization"]?.GetValue<string>() ?? "optional",
                        context?["user"]?.GetValue<string>() ?? "optional",
                        context?["provider"]?.GetValue<string>() ?? "optional");
                }
            }
        }

        return answers;
    }

    /// <summary>
    /// The answers in the contract a client assembly embeds as <c>openapi.json</c>.
    /// </summary>
    /// <param name="client">The client package's assembly.</param>
    /// <returns>The answers; none when the assembly embeds no contract.</returns>
    public static IReadOnlyDictionary<string, CacheableAnswerRead> FromAssembly(Assembly client)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var contract = client.GetManifestResourceStream("openapi.json");

        return contract is null ? new Dictionary<string, CacheableAnswerRead>() : FromContract(contract);
    }

    /// <summary>
    /// The owner's name, from the key template's prefix.
    /// </summary>
    internal string Owner => Key[..Math.Max(0, Key.IndexOf(':', StringComparison.Ordinal))];
}
