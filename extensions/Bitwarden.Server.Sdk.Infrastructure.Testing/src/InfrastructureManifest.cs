using System.Runtime.CompilerServices;
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

    private static readonly JsonSerializerOptions s_indentedOptions = new() { WriteIndented = true, NewLine = "\n" };
    private static readonly JsonSerializerOptions s_compactOptions = new();

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
    /// Writes the manifest as JSON with a fixed property order and <c>\n</c> line endings, so the
    /// output is the same on every platform and suitable for saving or snapshotting.
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

        return manifest.ToJsonString(indented ? s_indentedOptions : s_compactOptions);
    }

    /// <summary>
    /// Compares the manifest with the snapshot file at <paramref name="path"/>. When the snapshot is
    /// missing or out of date, writes the current manifest to it and throws, so the change can be
    /// reviewed and committed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A relative <paramref name="path"/> is resolved against the directory of the calling source
    /// file, so snapshots live next to the test that checks them. Commit one snapshot per hosting
    /// mode, for example <c>infrastructure.cloud.json</c> and <c>infrastructure.self-hosted.json</c>.
    /// </para>
    /// <para>
    /// Line endings in the existing snapshot are ignored, so a snapshot checked out with
    /// <c>\r\n</c> line endings still matches.
    /// </para>
    /// </remarks>
    /// <param name="path">The snapshot file, absolute or relative to the calling source file.</param>
    /// <param name="callerFilePath">Supplied by the compiler; do not pass a value.</param>
    /// <exception cref="InfrastructureSnapshotMismatchException">
    /// The snapshot was missing or differed from the manifest, and has been updated.
    /// </exception>
    public void VerifySnapshot(string path, [CallerFilePath] string callerFilePath = "")
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var snapshotPath = Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFilePath) ?? string.Empty, path));
        var expected = ToJsonString() + "\n";

        string? existing = null;
        if (File.Exists(snapshotPath))
        {
            existing = File.ReadAllText(snapshotPath).ReplaceLineEndings("\n");
            if (existing == expected)
            {
                return;
            }
        }

        var directory = Path.GetDirectoryName(snapshotPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(snapshotPath, expected);

        throw new InfrastructureSnapshotMismatchException(snapshotPath, existing is null);
    }
}
