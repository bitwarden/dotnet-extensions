namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// A document carrying exactly one resource.
/// </summary>
/// <remarks>
/// Lets the platform read the resource out of a document without knowing its generic argument,
/// which is needed to derive a created resource's <c>Location</c> from its identifier. Internal because it
/// exists for the platform's benefit and is not part of a service's vocabulary.
/// </remarks>
internal interface ISingleResourceDocument
{
    /// <summary>
    /// The resource the document carries.
    /// </summary>
    Resource Resource { get; }

    /// <summary>
    /// Where a created resource can be read, when its handler named somewhere else.
    /// </summary>
    string? Location => null;
}
