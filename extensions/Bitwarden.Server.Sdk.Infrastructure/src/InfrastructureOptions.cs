using System.Text.Json.Nodes;

namespace Bitwarden.Server.Sdk.Infrastructure;

/// <summary>
/// The infrastructure requirements declared by a service.
/// </summary>
/// <remarks>
/// <para>
/// Each requirement is a JSON object with a <c>kind</c> property, for example <c>database</c>, and
/// whatever other properties describe it. The whole object is the requirement's identity: adding a
/// requirement that is deeply equal to an existing one has no effect, while one that differs in any
/// property is kept as a separate requirement.
/// </para>
/// <para>
/// Requirements are added by configuring these options, either through
/// <see cref="Microsoft.Extensions.DependencyInjection.InfrastructureServiceCollectionExtensions.AddInfrastructureRequirement(Microsoft.Extensions.DependencyInjection.IServiceCollection, string, JsonObject?)"/>
/// or directly with the options pattern when a requirement depends on other services:
/// </para>
/// <code>
/// services.AddOptions&lt;InfrastructureOptions&gt;()
///     .Configure&lt;IBitwardenEnvironment&gt;((infrastructure, environment) =>
///     {
///         if (!environment.SelfHosted)
///         {
///             infrastructure.Add("distributed-cache");
///         }
///     });
/// </code>
/// <para>
/// The options are only built when something resolves them, which a running service normally
/// does not, so declarations cost nothing at runtime beyond their service descriptor.
/// </para>
/// </remarks>
public sealed class InfrastructureOptions
{
    private const string KindProperty = "kind";

    private readonly List<JsonObject> _requirements = [];

    /// <summary>
    /// The declared requirements, in the order they were first added.
    /// </summary>
    public IReadOnlyList<JsonObject> Requirements => _requirements;

    /// <summary>
    /// Adds a requirement of the given <paramref name="kind"/>, described by
    /// <paramref name="properties"/>, unless a deeply equal requirement has already been added.
    /// </summary>
    /// <param name="kind">The kind of infrastructure, for example <c>database</c>.</param>
    /// <param name="properties">
    /// Properties that describe the requirement, for example its name. The object is copied, so
    /// changing it afterwards does not affect the added requirement.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="properties"/> contains a <c>kind</c> property.</exception>
    public void Add(string kind, JsonObject? properties = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);

        var requirement = new JsonObject { [KindProperty] = kind };

        if (properties is not null)
        {
            foreach (var (key, value) in properties)
            {
                if (key == KindProperty)
                {
                    throw new ArgumentException($"Requirement properties may not contain '{KindProperty}'; pass it as the kind instead.", nameof(properties));
                }

                requirement[key] = value?.DeepClone();
            }
        }

        foreach (var existing in _requirements)
        {
            if (JsonNode.DeepEquals(existing, requirement))
            {
                return;
            }
        }

        _requirements.Add(requirement);
    }
}
