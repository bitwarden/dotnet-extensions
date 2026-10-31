using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// An action invoked upon every targeted resource, with input applied to each.
/// </summary>
/// <typeparam name="T">The resource being acted on.</typeparam>
/// <typeparam name="TInput">What the single-resource action takes as its body.</typeparam>
/// <remarks>
/// <see cref="Data"/> is exactly what the single-resource action accepts. An object is the same
/// body for every resource; an array is one body each, in the order <c>ids</c> named them, for
/// an action whose input the server cannot compute.
/// </remarks>
public sealed record BulkAction<T, TInput> : BulkTargeting<T> where T : Resource
{
    /// <summary>
    /// What the action is given: one body for every resource, or one body each.
    /// </summary>
    public BulkInput<TInput>? Data { get; init; }
}
