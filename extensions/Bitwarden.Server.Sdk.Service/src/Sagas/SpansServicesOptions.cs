using Bitwarden.Server.Sdk.Sagas;

namespace Bitwarden.Server.Sdk.Service.Sagas;

/// <summary>
/// What a service does when an operation's writes span services without saying how it handles a failure part way
/// (<c>SpansServices:*</c>, <see cref="SpansServicesAttribute"/>).
/// </summary>
public sealed class SpansServicesOptions
{
    /// <summary>
    /// The configuration section.
    /// </summary>
    public const string SectionName = "SpansServices";

    /// <summary>
    /// Refuse the write that makes an undeclared operation span services (the operation fails, a <c>500</c> for an
    /// endpoint) rather than only log and count it. Off by default. A test host turns it on, so a new one fails the
    /// test that first runs it; a development environment leaves it off, where a refusal would break a flow no test
    /// covers.
    /// </summary>
    public bool RefuseUndeclared { get; set; }
}
