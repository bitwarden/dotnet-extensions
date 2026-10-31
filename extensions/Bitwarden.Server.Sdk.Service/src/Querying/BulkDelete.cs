using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// The removal of every resource an expression matches.
/// </summary>
/// <typeparam name="T">The resource being removed.</typeparam>
/// <remarks>
/// The filter is required for the same reason as on an update, and more urgently: an absent
/// filter here would delete everything in scope.
/// </remarks>
public sealed record BulkDelete<T> : BulkTargeting<T> where T : Resource;
