using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// An action invoked upon every targeted resource, taking no input of its own.
/// </summary>
/// <typeparam name="T">The resource being acted on.</typeparam>
/// <remarks>
/// The bulk form of <c>{collection}/{id}/actions/{action}</c>, and shorthand for calling it
/// once per resource. It is never a way around the rules that action applies.
/// </remarks>
public sealed record BulkAction<T> : BulkTargeting<T> where T : Resource;
