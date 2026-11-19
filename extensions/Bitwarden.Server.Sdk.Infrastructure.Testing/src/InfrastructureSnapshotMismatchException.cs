namespace Bitwarden.Server.Sdk.Infrastructure.Testing;

/// <summary>
/// Thrown by <see cref="InfrastructureManifest.VerifySnapshot"/> when a snapshot was missing or out of
/// date. The snapshot file has already been updated with the current manifest.
/// </summary>
public sealed class InfrastructureSnapshotMismatchException : Exception
{
    internal InfrastructureSnapshotMismatchException(string snapshotPath, bool created)
        : base(created
            ? $"Infrastructure snapshot '{snapshotPath}' did not exist and has been created. Review and commit it."
            : $"Infrastructure snapshot '{snapshotPath}' was out of date and has been updated. Review and commit the changes.")
    {
        SnapshotPath = snapshotPath;
        Created = created;
    }

    /// <summary>
    /// The snapshot file that was written.
    /// </summary>
    public string SnapshotPath { get; }

    /// <summary>
    /// Whether the snapshot did not exist before, rather than being out of date.
    /// </summary>
    public bool Created { get; }
}
