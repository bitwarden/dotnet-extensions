using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace Bitwarden.Server.Sdk.Infrastructure;

/// <summary>
/// The infrastructure requirements declared by a service.
/// </summary>
/// <remarks>
/// <para>
/// Each requirement has a kind, for example <c>database</c>, and a JSON object of properties that
/// describe it. Within a kind, the properties are the requirement's identity: adding a requirement
/// whose properties are deeply equal to an existing one of the same kind has no effect, while one
/// that differs in any property is kept as a separate requirement.
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
    private readonly Dictionary<string, List<JsonObject>> _requirements = [];
    private readonly SortedDictionary<string, IReadOnlyList<JsonObject>> _requirementsByKind = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="InfrastructureOptions"/> class.
    /// </summary>
    public InfrastructureOptions()
    {
        Requirements = new ReadOnlyDictionary<string, IReadOnlyList<JsonObject>>(_requirementsByKind);
    }

    /// <summary>
    /// The declared requirements grouped by kind. Kinds are in ordinal order, and the requirements
    /// of each kind are in the order they were first added.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<JsonObject>> Requirements { get; }

    /// <summary>
    /// Adds a requirement of the given <paramref name="kind"/>, described by
    /// <paramref name="properties"/>, unless one of the same kind with deeply equal properties has
    /// already been added.
    /// </summary>
    /// <param name="kind">The kind of infrastructure, for example <c>database</c>.</param>
    /// <param name="properties">
    /// Properties that describe the requirement, for example its name. The object is copied, so
    /// changing it afterwards does not affect the added requirement.
    /// </param>
    public void Add(string kind, JsonObject? properties = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);

        var requirement = (JsonObject?)properties?.DeepClone() ?? [];

        if (!_requirements.TryGetValue(kind, out var requirements))
        {
            requirements = [];
            _requirements.Add(kind, requirements);
            _requirementsByKind.Add(kind, requirements.AsReadOnly());
        }

        foreach (var existing in requirements)
        {
            if (JsonNode.DeepEquals(existing, requirement))
            {
                return;
            }
        }

        requirements.Add(requirement);
    }
}
