using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bitwarden.Server.Sdk.Infrastructure.Testing;

/// <summary>
/// The infrastructure a service declared for one hosting mode, as returned by
/// <see cref="InfrastructureCollector.CollectInfrastructure{TEntryPoint}"/>.
/// </summary>
public sealed class InfrastructureManifest
{
    /// <summary>
    /// The version of the manifest format written by <see cref="ToJsonString"/>. Incremented when the
    /// shape of the manifest or of its requirements changes in a way readers need to know about.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    internal InfrastructureManifest(bool selfHosted, string? selfHostFlavor, string environment, IReadOnlyDictionary<string, IReadOnlyList<JsonObject>> requirements)
    {
        SelfHosted = selfHosted;
        SelfHostFlavor = selfHostFlavor;
        Environment = environment;
        Requirements = requirements;
    }

    /// <summary>
    /// The version of the manifest format, always <see cref="CurrentSchemaVersion"/>.
    /// </summary>
    public int SchemaVersion => CurrentSchemaVersion;

    /// <summary>
    /// Whether the service was run as self-hosted.
    /// </summary>
    public bool SelfHosted { get; }

    /// <summary>
    /// The self-host flavor the service was run as, or <see langword="null"/> when run as cloud.
    /// </summary>
    public string? SelfHostFlavor { get; }

    /// <summary>
    /// The hosting environment the service was run in.
    /// </summary>
    public string Environment { get; }

    /// <summary>
    /// The declared requirements grouped by kind. Kinds are in ordinal order, and the requirements
    /// of each kind are in the order they were first declared.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<JsonObject>> Requirements { get; }

    /// <summary>
    /// Writes the manifest as JSON with a fixed property order, suitable for saving or snapshotting.
    /// </summary>
    /// <param name="indented">Whether to indent the JSON.</param>
    /// <returns>The manifest as JSON.</returns>
    public string ToJsonString(bool indented = true)
    {
        var requirements = new JsonObject();
        foreach (var (kind, requirementsOfKind) in Requirements.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var array = new JsonArray();
            foreach (var requirement in requirementsOfKind)
            {
                array.Add(requirement.DeepClone());
            }

            requirements[kind] = array;
        }

        var manifest = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["selfHosted"] = SelfHosted,
            ["selfHostFlavor"] = SelfHostFlavor,
            ["environment"] = Environment,
            ["requirements"] = requirements,
        };

        return manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = indented });
    }
}
