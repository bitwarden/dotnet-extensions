namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// A document whose resources may carry only some of their attributes.
/// </summary>
internal interface ISparseDocument
{
    /// <summary>
    /// The attributes each resource carries, or null for every one.
    /// </summary>
    IReadOnlySet<string>? Fields { get; }
}
