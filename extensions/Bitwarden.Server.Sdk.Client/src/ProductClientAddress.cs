namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// The address a product client found by convention, read as the host starts, so a plain address where the deployment
/// requires TLS stops the host before a person's token is sent over it.
/// </summary>
internal sealed class ProductClientAddress
{
    /// <summary>
    /// The address found, empty for none.
    /// </summary>
    public string BaseAddress { get; set; } = string.Empty;

    /// <summary>
    /// The deployment's <c>Tls:RequireHttps</c>.
    /// </summary>
    public bool RequireHttps { get; set; }

    /// <summary>
    /// Whether the address may be used: any address where TLS isn't required, and otherwise none or an
    /// <c>https://</c> one.
    /// </summary>
    public bool Allowed =>
        !RequireHttps
        || string.IsNullOrWhiteSpace(BaseAddress)
        || (Uri.TryCreate(BaseAddress, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps);
}
