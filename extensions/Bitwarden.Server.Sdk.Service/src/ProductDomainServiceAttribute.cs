namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Marks a <see cref="Service"/> as a product domain service: a domain service that owns one
/// product's data, rather than a platform service every product shares.
/// </summary>
/// <remarks>
/// <para>
/// Changes the service's API documentation only: its Swagger UI renders dark on a background of
/// its own, as a product service's does on another, so the three kinds can be told apart at a glance.
/// Everything else about the service is a domain service's: only other services call it, and the
/// acting user is whatever the caller asserts in <see cref="Common.BitwardenHeaders.User"/>.
/// </para>
/// <para>
/// A service can't be both this and a <see cref="ProductServiceAttribute"/>: the constructor
/// refuses the pair. Inherited, as <see cref="ProductServiceAttribute"/> is.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [ProductDomainService]
/// internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ProductDomainServiceAttribute : Attribute;
