namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// A page of resources, and how many there are in total.
/// </summary>
/// <typeparam name="T">The resource.</typeparam>
/// <param name="Items">The resources on this page.</param>
/// <param name="Total">How many there are across every page.</param>
/// <remarks>
/// What a query returns, and what the platform needs to build the response's links. A query
/// reports the total because only it knows how to count what it filtered.
/// </remarks>
public readonly record struct Paged<T>(IReadOnlyList<T> Items, int Total);
