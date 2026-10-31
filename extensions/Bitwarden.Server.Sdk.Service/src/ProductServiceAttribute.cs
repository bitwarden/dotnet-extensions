namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Marks a <see cref="Service"/> as a product service: people call it directly, holding their own
/// session.
/// </summary>
/// <remarks>
/// <para>
/// Absent, the default, for a domain service: only other services call it, and the acting user is
/// whatever the caller asserts in <see cref="Common.BitwardenHeaders.User"/>.
/// </para>
/// <para>
/// A product service is where a person's session ends. Applying this attribute changes three
/// things and nothing else:
/// </para>
/// <list type="bullet">
///   <item><description>
///     The acting user (<see cref="Common.IContext.UserId"/>, and so row ownership and the
///     <see cref="AuthorizationAttribute.User"/> requirement) is the validated session token's
///     <c>sub</c>.
///   </description></item>
///   <item><description>
///     A request carrying <see cref="Common.BitwardenHeaders.User"/> is refused <c>400</c>, and a
///     token without a <c>sub</c> is refused <c>401</c>.
///   </description></item>
///   <item><description>
///     The published document declares the session bearer token and stops offering the header as a
///     credential.
///   </description></item>
/// </list>
/// <para>
/// What reaches the domain services follows without anything further: a call made through a
/// generated client carries the acting user in the header, read from <see cref="Common.IContext"/>,
/// and this service's own <c>internal.{name}</c> credential. The person's token is never forwarded.
/// Scope checks are unchanged, and are made against the person's token.
/// </para>
/// <para>
/// Read once, from the concrete type and its bases, when the service is constructed. Inherited,
/// because losing it is the unsafe direction: a type deriving from a product service that did not
/// inherit it would quietly become a domain service and believe whatever user a caller asserts in
/// the header. Inherited, a derived type can only be as strict as its base. The answer is
/// published as
/// <see cref="Common.IServiceIdentity.IsProductService"/> for anything hosting or testing the
/// service.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [ProductService]
/// internal sealed class WidgetPortal(string[] args)
///     : Service("widget-portal", "Widget Portal", args);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ProductServiceAttribute : Attribute;
