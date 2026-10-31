namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// A request body naming the provider its action is about (<see cref="NamesProviderAttribute"/>).
/// </summary>
public interface INamesProvider
{
    /// <summary>
    /// The provider the action is about.
    /// </summary>
    Guid ProviderId { get; }
}
