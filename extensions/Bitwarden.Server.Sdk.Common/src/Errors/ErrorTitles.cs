namespace Bitwarden.Server.Sdk.Common.Errors;

/// <summary>
/// The short summaries that accompany each error code.
/// </summary>
/// <remarks>
/// A title says what kind of thing went wrong and never varies with the occurrence. Anything
/// specific to the request belongs in <see cref="ErrorDetails"/>.
/// </remarks>
public static class ErrorTitles
{
    /// <summary>
    /// Title for <see cref="ErrorCodes.MalformedJson"/>.
    /// </summary>
    public const string MalformedJson = "The request body could not be parsed.";

    /// <summary>
    /// Title for <see cref="ErrorCodes.InvalidShape"/>.
    /// </summary>
    public const string InvalidShape = "The request body is not the expected shape.";

    /// <summary>
    /// Title for <see cref="ErrorCodes.InvalidRequest"/>.
    /// </summary>
    public const string InvalidRequest = "The request cannot be handled.";

    /// <summary>
    /// Title for <see cref="ErrorCodes.MethodNotAllowed"/>.
    /// </summary>
    public const string MethodNotAllowed = "The method is not allowed on this resource.";

    /// <summary>
    /// Title for <see cref="ErrorCodes.UnsupportedMediaType"/>.
    /// </summary>
    public const string UnsupportedMediaType = "The request body's media type is not supported.";

    /// <summary>
    /// Title for <see cref="ErrorCodes.NotAcceptable"/>.
    /// </summary>
    public const string NotAcceptable = "No acceptable representation is available.";

    /// <summary>
    /// Title for <see cref="ErrorCodes.FieldRequired"/>.
    /// </summary>
    public const string FieldRequired = "A required field is missing.";

    /// <summary>
    /// Title for <see cref="ErrorCodes.InvalidType"/>.
    /// </summary>
    public const string InvalidType = "A field has the wrong type.";

    /// <summary>
    /// Title for <see cref="ErrorCodes.InvalidValue"/>.
    /// </summary>
    public const string InvalidValue = "A field has an invalid value.";

    /// <summary>
    /// Title for <see cref="ErrorCodes.InvalidResourceType"/>.
    /// </summary>
    public const string InvalidResourceType = "The resource type is not the one this endpoint serves.";

    /// <summary>
    /// Title for <see cref="ErrorCodes.InternalError"/>.
    /// </summary>
    public const string InternalError = "The request could not be completed.";

    /// <summary>
    /// Title for <see cref="ErrorCodes.NotFound"/>.
    /// </summary>
    public const string NotFound = "The resource does not exist.";

    /// <summary>
    /// Title for <see cref="ErrorCodes.NotImplemented"/>.
    /// </summary>
    public const string NotImplemented = "The operation is not implemented.";

    /// <summary>
    /// Title for <see cref="ErrorCodes.ServiceUnavailable"/>.
    /// </summary>
    public const string ServiceUnavailable = "A service this depends on is unavailable.";

    /// <summary>
    /// Title for <see cref="ErrorCodes.Conflict"/>.
    /// </summary>
    public const string Conflict = "The request conflicts with the current state.";

    /// <summary>
    /// Title for <see cref="ErrorCodes.AlreadyExists"/>.
    /// </summary>
    public const string AlreadyExists = "A value is already in use.";

    /// <summary>
    /// Title for <see cref="ErrorCodes.Unauthenticated"/>.
    /// </summary>
    public const string Unauthenticated = "The caller is not authenticated.";

    /// <summary>
    /// Title for <see cref="ErrorCodes.Forbidden"/>.
    /// </summary>
    public const string Forbidden = "The caller is not permitted to do this.";

    /// <summary>
    /// Title for <see cref="ErrorCodes.KeyRotationInProgress"/>.
    /// </summary>
    public const string KeyRotationInProgress = "A key rotation is in progress.";
}
