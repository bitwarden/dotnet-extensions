namespace Bitwarden.Server.Sdk.Common.Errors;

/// <summary>
/// The stable, machine-readable codes a caller branches on.
/// </summary>
/// <remarks>
/// Centralised because these are contract. A title or a detail may be reworded or localised; a
/// code may not change without breaking every caller that switches on it.
/// </remarks>
public static class ErrorCodes
{
    /// <summary>The request body is not valid JSON.</summary>
    public const string MalformedJson = "malformed-json";

    /// <summary>The request body is valid JSON but not the shape the API expects.</summary>
    public const string InvalidShape = "invalid-shape";

    /// <summary>The request is unacceptable for a reason with no more specific code.</summary>
    public const string InvalidRequest = "invalid-request";

    /// <summary>The resource exists but does not support the method used on it.</summary>
    public const string MethodNotAllowed = "method-not-allowed";

    /// <summary>The request body was sent in a media type the API does not read.</summary>
    public const string UnsupportedMediaType = "unsupported-media-type";

    /// <summary>The caller asked for a representation the API cannot produce.</summary>
    public const string NotAcceptable = "not-acceptable";

    /// <summary>A required field was absent, null, or empty.</summary>
    public const string FieldRequired = "field-required";

    /// <summary>A field carried a value of the wrong type.</summary>
    public const string InvalidType = "invalid-type";

    /// <summary>A field's value is the right type but not acceptable.</summary>
    public const string InvalidValue = "invalid-value";

    /// <summary>The resource declared a type other than the one the endpoint serves.</summary>
    public const string InvalidResourceType = "invalid-resource-type";

    /// <summary>The service failed in a way the caller cannot correct.</summary>
    public const string InternalError = "internal-error";

    /// <summary>The resource addressed by the URL does not exist, or may not be known to exist.</summary>
    public const string NotFound = "not-found";

    /// <summary>The operation exists in the contract but has no implementation behind it.</summary>
    public const string NotImplemented = "not-implemented";

    /// <summary>A service this one depends on could not be reached, or said it was unavailable.</summary>
    public const string ServiceUnavailable = "service-unavailable";

    /// <summary>The request conflicts with the resource's current state.</summary>
    public const string Conflict = "conflict";

    /// <summary>A value is already in use by another resource.</summary>
    public const string AlreadyExists = "already-exists";

    /// <summary>The caller presented no credential, or one that is not valid.</summary>
    public const string Unauthenticated = "unauthenticated";

    /// <summary>The caller is known but may not perform this operation.</summary>
    public const string Forbidden = "forbidden";

    /// <summary>
    /// The person's keys are rotating across services: a
    /// write is refused (<c>409</c>) and a read that could mix key generations asks the caller to
    /// retry (<c>503</c>). Passed back unchanged by every service in between, so the client learns
    /// why, not just that something downstream was unavailable.
    /// </summary>
    public const string KeyRotationInProgress = "key-rotation-in-progress";
}
