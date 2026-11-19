using System.Text.Json.Nodes;

namespace Bitwarden.Server.Sdk.MessageBroker.Topology;

/// <summary>
/// One service's infrastructure manifest, as <c>InfrastructureManifest.ToJsonString</c> writes it, under the name
/// the topology gives the service: the owner of its topics and queues, and the holder of its rights.
/// </summary>
public sealed class ServiceManifest
{
    /// <summary>
    /// The manifest format this reads: <c>InfrastructureManifest.CurrentSchemaVersion</c>.
    /// </summary>
    public const int SchemaVersion = 1;

    private ServiceManifest(string service, IReadOnlyDictionary<string, IReadOnlyList<JsonObject>> requirements)
    {
        Service = service;
        Requirements = requirements;
    }

    /// <summary>
    /// The service's name.
    /// </summary>
    public string Service { get; }

    /// <summary>
    /// The service's requirements by kind, as declared.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<JsonObject>> Requirements { get; }

    /// <summary>
    /// Reads a manifest's JSON.
    /// </summary>
    /// <param name="service">The service's name.</param>
    /// <param name="json">The manifest, as <c>InfrastructureManifest.ToJsonString</c> writes it.</param>
    /// <exception cref="FormatException">The JSON isn't a manifest of <see cref="SchemaVersion"/>.</exception>
    public static ServiceManifest Parse(string service, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new FormatException($"{service}'s manifest isn't a JSON object.");
        if (root["schemaVersion"]?.GetValueKind() != System.Text.Json.JsonValueKind.Number
            || root["schemaVersion"]!.GetValue<int>() != SchemaVersion)
        {
            throw new FormatException($"{service}'s manifest isn't schema version {SchemaVersion}.");
        }

        var requirements = new Dictionary<string, IReadOnlyList<JsonObject>>(StringComparer.Ordinal);
        if (root["requirements"] is JsonObject kinds)
        {
            foreach (var (kind, list) in kinds)
            {
                requirements[kind] = list is JsonArray array
                    ? [.. array.Select(item => item as JsonObject
                        ?? throw new FormatException($"{service}'s {kind} requirements must be JSON objects."))]
                    : throw new FormatException($"{service}'s {kind} requirements must be a JSON array.");
            }
        }

        return new ServiceManifest(service, requirements);
    }

    /// <summary>
    /// Makes a manifest from requirements already read, such as <c>InfrastructureOptions.Requirements</c>.
    /// </summary>
    /// <param name="service">The service's name.</param>
    /// <param name="requirements">The service's requirements by kind.</param>
    public static ServiceManifest FromRequirements(string service, IReadOnlyDictionary<string, IReadOnlyList<JsonObject>> requirements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        ArgumentNullException.ThrowIfNull(requirements);
        return new ServiceManifest(service, requirements);
    }

    internal IEnumerable<IReadOnlyDictionary<string, string>> Of(string kind, params string[] properties)
    {
        if (!Requirements.TryGetValue(kind, out var list))
        {
            yield break;
        }

        foreach (var requirement in list)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in properties)
            {
                values[property] = requirement[property] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0
                    ? text
                    : throw new FormatException($"{Service}'s {kind} requirement {requirement.ToJsonString()} has no {property}.");
            }

            yield return values;
        }
    }
}
