using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Features;

/// <summary>
/// What a client needs to know about the platform before and after it signs in: which version
/// answered, where things are, how the deployment is set up, and which features are on.
/// </summary>
/// <remarks>
/// <para>
/// A singleton with no identifier. There is one per service, and its type names it. Read only:
/// nothing writes it, so its members carry no <see cref="ReadOnlyAttribute"/>.
/// </para>
/// <para>
/// <c>version</c> is the inherited resource version, holding the version of the service that
/// answered. The configuration changes when the service does, so it is the version of this
/// resource too.
/// </para>
/// <para>
/// Nothing here is secret. It is the same for every caller except <see cref="FeatureFlags"/>,
/// whose values may be targeted at the caller, and those are switches, not data.
/// </para>
/// </remarks>
[ResourceType("config")]
internal sealed record Config : Resource
{
    /// <summary>
    /// Where this deployment is and what it links to.
    /// </summary>
    public required ConfigEnvironment Environment { get; init; }

    /// <summary>
    /// How this deployment is set up.
    /// </summary>
    public required ConfigSettings Settings { get; init; }

    /// <summary>
    /// The value of each feature flag, by key, as evaluated for the caller. A value is a boolean,
    /// a number or a string. A flag the platform has no value for is absent.
    /// </summary>
    /// <remarks>
    /// Declared as a map of any value, which is what it is on the wire. The package's own
    /// <see cref="JsonValue"/> would publish as an object with no members, and a client generated
    /// from that has nowhere to put the flags.
    /// </remarks>
    public required IReadOnlyDictionary<string, object> FeatureFlags { get; init; }
}
