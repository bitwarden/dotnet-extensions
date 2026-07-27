namespace Bitwarden.Server.Sdk.Environment.Setup;

/// <summary>
/// Options class used to configure whether the current instance is self-hosted and its deployment flavor.
/// </summary>
public sealed class SelfHostDetails
{
    /// <summary>
    /// Gets a value indicating whether this instance is self-hosted.
    /// </summary>
    public bool SelfHosted { get; private set; }

    /// <summary>
    /// Gets the self-host flavor (e.g., <c>"lite"</c>), or <see langword="null"/> for cloud instances.
    /// </summary>
    public string? SelfHostFlavor { get; private set; }

    /// <summary>
    /// Configures this instance as a cloud (non-self-hosted) deployment.
    /// </summary>
    public void MakeCloud()
    {
        SelfHosted = false;
        SelfHostFlavor = null;
    }

    /// <summary>
    /// Configures this instance as a self-hosted deployment with the specified flavor.
    /// </summary>
    /// <param name="flavor">The self-host flavor (e.g., <c>"lite"</c>).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="flavor"/> is null, empty, or whitespace.</exception>
    public void MakeSelfHost(string flavor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flavor);
        SelfHosted = true;
        SelfHostFlavor = flavor;
    }
}
