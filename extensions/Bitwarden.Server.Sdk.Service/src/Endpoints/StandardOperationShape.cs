using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// What an <see cref="IStandardOperation"/> attribute settles about its operation: the identifier,
/// the documented response and the status it succeeds with.
/// </summary>
/// <remarks>
/// <para>
/// Three things an author would otherwise have to write on every handler:
/// </para>
/// <list type="bullet">
///   <item><description>
///     The <b>operation identifier</b>, derived from the method name and the route's version:
///     <c>CreateUser</c> on <c>api/v1/users</c> becomes <c>createUserV1</c>. The standards require
///     it to be explicit and version-qualified; deriving it makes it impossible to forget and
///     impossible to get inconsistently right.
///   </description></item>
///   <item><description>
///     The <b>documented response</b>, taken from the handler's own return type by reflection, at
///     the status code the operation kind succeeds with. So no <c>[ProducesResponseType]</c>, and
///     the document cannot disagree with the signature.
///   </description></item>
///   <item><description>
///     The <b>status code at runtime</b>, which documentation alone does not set. Returning a value
///     from a handler is a <c>200</c> unless something says otherwise.
///   </description></item>
/// </list>
/// <para>
/// <see cref="StandardEndpointConvention"/> applies each only where the author has not been
/// explicit, so adding <c>[ProducesResponseType]</c> or <c>[EndpointName]</c> by hand always wins.
/// </para>
/// </remarks>
internal static class StandardOperationShape
{
    /// <summary>
    /// Matches a major-version path segment, as the standards mandate: <c>v1</c>, <c>v2</c>.
    /// </summary>
    private static readonly Regex VersionSegment = new(@"(?:^|/)(v\d+)(?:/|$)", RegexOptions.Compiled);

    /// <summary>
    /// The version an operation identifier leaves unsaid.
    /// </summary>
    private const string FirstVersion = "v1";

    /// <summary>
    /// The status code an operation succeeds with.
    /// </summary>
    /// <remarks>
    /// An action is the one operation kind whose result varies: two actions on the same
    /// resource may differ in whether they have anything to say back. Every other kind
    /// always does or never does, and answers for itself.
    /// </remarks>
    internal static int SuccessStatus(IStandardOperation standard, Type? responseType) =>
        standard is StandardActionAttribute && responseType is null
            ? StatusCodes.Status204NoContent
            : standard.SuccessStatusCode;

    /// <summary>
    /// Reduces a handler's declared return type to the type that reaches the wire.
    /// </summary>
    /// <remarks>
    /// Unwraps the asynchrony and result wrappers an author may use (<c>Task&lt;T&gt;</c>,
    /// <c>ActionResult&lt;T&gt;</c>) and reports nothing for a handler that returns no value, so a
    /// delete documents a bodiless response rather than a body of type <c>void</c>.
    /// </remarks>
    internal static Type? UnwrapReturnType(Type returnType)
    {
        if (returnType == typeof(void) || returnType == typeof(Task) || returnType == typeof(ValueTask))
        {
            return null;
        }

        if (returnType.IsGenericType)
        {
            var definition = returnType.GetGenericTypeDefinition();

            if (definition == typeof(Task<>) || definition == typeof(ValueTask<>) || definition == typeof(ActionResult<>))
            {
                return UnwrapReturnType(returnType.GetGenericArguments()[0]);
            }
        }

        // A bare IActionResult carries no type information; leave the response untyped rather than
        // documenting the interface.
        return typeof(IActionResult).IsAssignableFrom(returnType) ? null : returnType;
    }

    /// <summary>
    /// The version suffix the first versioned template carries; empty for none or <c>v1</c>.
    /// </summary>
    /// <remarks>
    /// Empty when the route carries no version, which is the right answer for an operational
    /// endpoint outside <c>/api</c> and a signal that a resource route is missing its version.
    /// </remarks>
    internal static string VersionSuffix(IEnumerable<string?> templates)
    {
        foreach (var template in templates)
        {
            if (template is not null && VersionSegment.Match(template) is { Success: true } match)
            {
                var version = match.Groups[1].Value;

                // v1 is the unmarked case. Every operation carrying a "V1" would say only that
                // nothing has been versioned yet, and a caller reading "createUser" against
                // "createUserV2" can see at a glance which is which.
                return version == FirstVersion
                    ? string.Empty
                    : char.ToUpperInvariant(version[0]) + version[1..];
            }
        }

        return string.Empty;
    }

    internal static string CamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
