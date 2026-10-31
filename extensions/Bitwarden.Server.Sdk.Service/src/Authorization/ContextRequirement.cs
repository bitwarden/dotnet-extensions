namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Whether a context member must be present, may be present, or must be absent.
/// </summary>
public enum ContextRequirement
{
    /// <summary>
    /// Nothing said here; whatever the handler's class or the service says.
    /// </summary>
    /// <remarks>
    /// Zero, so an attribute that never mentions a requirement inherits rather than asserting
    /// the platform's default. That is what lets an operation state only where it differs from
    /// the class it is declared in.
    /// </remarks>
    Unset = 0,

    /// <summary>
    /// The caller must supply it; a request without it is refused.
    /// </summary>
    Required,

    /// <summary>
    /// The caller may supply it. The operation behaves sensibly either way.
    /// </summary>
    Optional,

    /// <summary>
    /// The caller must not supply it; a request carrying it is refused.
    /// </summary>
    Forbidden,
}
