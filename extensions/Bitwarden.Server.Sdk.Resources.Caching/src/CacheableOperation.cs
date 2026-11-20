using Bitwarden.Server.Sdk.Authorization;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// What a cacheable read's operation demands, as the platform settled it.
/// </summary>
/// <param name="Scope">The scope it demands.</param>
/// <param name="Organization">Whether a request must, may or must not name an organization.</param>
/// <param name="User">Likewise, an acting user.</param>
/// <param name="Provider">Likewise, a provider.</param>
/// <param name="Current">Whether it reads <c>current</c>, its key named by the context rather than the request.</param>
/// <param name="AssertedBy">
/// The services whose own assertion of a context naming a person it admits (<c>[AcceptsAssertedContext]</c>); none when
/// it admits only a person's own, delegated, context.
/// </param>
internal sealed record CacheableOperation(
    string Scope,
    ContextRequirement Organization,
    ContextRequirement User,
    ContextRequirement Provider,
    bool Current = false,
    IReadOnlyList<string>? AssertedBy = null);
