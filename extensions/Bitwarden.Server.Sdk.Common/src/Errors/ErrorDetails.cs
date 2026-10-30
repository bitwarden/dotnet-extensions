namespace Bitwarden.Server.Sdk.Common.Errors;

/// <summary>
/// The occurrence-specific explanations that accompany each error.
/// </summary>
/// <remarks>
/// A detail names what was wrong with this request, so most are parameterized.
/// </remarks>
public static class ErrorDetails
{
    /// <summary>
    /// Detail for a body that is absent or empty.
    /// </summary>
    public const string EmptyBody = "The request body is empty.";

    /// <summary>
    /// Detail for a body that is not parseable as JSON.
    /// </summary>
    public const string MalformedJson = "The request body is not valid JSON.";

    /// <summary>
    /// Detail for a body that parsed but is not a JSON object.
    /// </summary>
    public const string NotAnObject = "The request body must be a JSON object.";

    /// <summary>
    /// Detail for a request rejected before it reached an operation.
    /// </summary>
    public const string InvalidRequest = "The request cannot be handled as sent.";

    /// <summary>
    /// Detail for a method the addressed resource does not support.
    /// </summary>
    /// <param name="method">The method that was used.</param>
    public static string MethodNotAllowed(string method) =>
        $"The resource does not support {method} requests.";

    /// <summary>
    /// Detail for a body sent in a media type the API does not read.
    /// </summary>
    public const string UnsupportedMediaType = "The request body must be sent as application/json.";

    /// <summary>
    /// Detail for a representation the API cannot produce.
    /// </summary>
    public const string NotAcceptable = "This API produces application/json only.";

    /// <summary>
    /// Detail for a route that matches nothing.
    /// </summary>
    public const string NoSuchRoute = "No resource exists at this address.";

    /// <summary>
    /// Detail for a required field that is absent, null, or empty.
    /// </summary>
    /// <param name="field">The field's name as it appears on the wire.</param>
    public static string FieldRequired(string field) => $"The '{field}' field is required.";

    /// <summary>
    /// Detail for a value outside the set a field permits.
    /// </summary>
    /// <param name="field">The field's name as it appears on the wire.</param>
    /// <param name="permitted">The values it accepts.</param>
    public static string NotAPermittedValue(string field, IReadOnlyList<string> permitted) =>
        $"The '{field}' field must be one of: {string.Join(", ", permitted)}.";

    /// <summary>
    /// Detail for a field carrying the wrong JSON type.
    /// </summary>
    /// <param name="field">The field's name as it appears on the wire.</param>
    /// <param name="expected">What the field should have been, for example <c>a string</c>.</param>
    public static string InvalidType(string field, string expected) =>
        $"The '{field}' field must be {expected}.";

    /// <summary>
    /// Detail for an unexpected failure.
    /// </summary>
    /// <remarks>
    /// Says nothing about the fault. The error's <c>id</c> ties the response to the log entry that
    /// carries the detail.
    /// </remarks>
    public const string InternalError =
        "An unexpected error occurred. Quote the error id when reporting this.";

    /// <summary>
    /// Detail for a resource the URL addresses but that does not exist.
    /// </summary>
    public const string NotFound = "The requested resource does not exist.";

    /// <summary>
    /// Detail for a named resource that does not exist.
    /// </summary>
    /// <param name="resourceType">The resource type, singular, for example <c>user</c>.</param>
    /// <param name="id">The ID that was requested.</param>
    public static string NotFoundById(string resourceType, string id) =>
        $"No {resourceType} with ID '{id}' exists.";

    /// <summary>
    /// Detail for a request carrying no credential, or one that is not valid.
    /// </summary>
    public const string Unauthenticated = "A valid access token is required.";

    /// <summary>
    /// Detail for a caller who is known but not permitted.
    /// </summary>
    public const string Forbidden = "The caller is not permitted to perform this operation.";

    /// <summary>
    /// Detail for a resource that changed since the caller last read it.
    /// </summary>
    public const string Conflict =
        "The resource has changed since it was last read. Read it again and retry.";

    /// <summary>
    /// Detail for a value that must be unique but is already taken.
    /// </summary>
    /// <param name="field">The field's name as it appears on the wire.</param>
    public static string AlreadyExists(string field) => $"The '{field}' value is already in use.";

    /// <summary>
    /// Detail for an operation that has been declared but not written.
    /// </summary>
    public const string NotImplemented = "This operation has not been implemented yet.";

    /// <summary>
    /// Detail for a resource declaring a type the endpoint does not serve.
    /// </summary>
    /// <param name="expected">The resource type the endpoint serves.</param>
    /// <param name="actual">The resource type the caller declared.</param>
    public static string InvalidResourceType(string expected, string actual) =>
        $"A resource of type '{expected}' was expected, but '{actual}' was declared.";

    /// <summary>
    /// Detail for a request refused while the person's keys are rotating.
    /// </summary>
    public const string KeyRotationInProgress =
        "The account's encryption keys are being rotated. Retry once the rotation has finished.";
}
