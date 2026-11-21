namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// A cacheable query, as the catalog declares it.
/// </summary>
internal interface ICacheableQuery
{
    /// <summary>
    /// Runs the query's <c>Configure</c>, and checks what it declared.
    /// </summary>
    ICacheableResource Declare();
}
