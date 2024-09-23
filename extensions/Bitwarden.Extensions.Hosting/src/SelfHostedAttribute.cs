namespace Bitwarden.Extensions.Hosting;

/// <summary>
/// Attribute to indicate that an instance is self-hosted.
/// </summary>
public class SelfHostedAttribute : Attribute
{
    // TODO: Need to try and build this so it works for both MVC and Minimal APIs
    // Also maybe we lie about the namespace so we don't have to update all those files?
}
