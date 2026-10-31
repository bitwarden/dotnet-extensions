namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// A JSON:API resource object: the thing that appears under a document's <c>data</c>.
/// </summary>
/// <remarks>
/// <para>
/// A derived resource declares its fields as ordinary properties, flat, with no nested attributes
/// object. The <c>attributes</c> envelope the API standards require is applied on the wire by
/// <see cref="ResourceDocumentJsonConverterFactory"/>, so the JSON is compliant while the model a developer
/// writes and reads stays flat. The OpenAPI document describes the wire shape, not this one.
/// </para>
/// <para>
/// <see cref="Type"/> is derived from the derived record's own name (<c>User</c> gives
/// <c>user</c>), so a resource declares no constructor and makes no base call.
/// <see cref="ResourceTypeAttribute"/> overrides it where convention is wrong.
/// </para>
/// <para>
/// A record rather than a class: resources are data, compared by value, built once and serialised.
/// A record can only derive from a record, so every resource in the hierarchy is one.
/// </para>
/// </remarks>
public abstract record Resource
{
    /// <summary>
    /// Derives <see cref="Type"/> from the runtime type of the resource being constructed.
    /// </summary>
    protected Resource() => Type = ResourceTypes.For(GetType());

    /// <summary>
    /// The resource's identifier. Absent on a resource being created, where the server assigns it.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// The resource type: the singular name of the resource, such as <c>user</c>.
    /// </summary>
    public string Type { get; init; }

    /// <summary>
    /// The date and time the resource was created, in ISO 8601.
    /// </summary>
    [ReadOnly]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// The ID of the user that created the resource.
    /// </summary>
    [ReadOnly]
    public string? CreatedBy { get; init; }

    /// <summary>
    /// The date and time the resource was deleted, in ISO 8601.
    /// </summary>
    [ReadOnly]
    public DateTimeOffset? DeletedAt { get; init; }

    /// <summary>
    /// The ID of the user that deleted the resource.
    /// </summary>
    [ReadOnly]
    public string? DeletedBy { get; init; }

    /// <summary>
    /// The date and time the resource was last updated, in ISO 8601.
    /// </summary>
    [ReadOnly]
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// The ID of the user that last updated the resource.
    /// </summary>
    [ReadOnly]
    public string? UpdatedBy { get; init; }

    /// <summary>
    /// The current version of the resource.
    /// </summary>
    public string? Version { get; init; }
}
