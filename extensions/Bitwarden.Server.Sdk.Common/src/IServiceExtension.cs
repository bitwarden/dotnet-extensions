using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// A platform package's contribution to every service that references it.
/// </summary>
/// <remarks>
/// <para>
/// The seam that lets a package configure a service without the service asking. The outbox is
/// the case that forced it: every service with a database must publish through one, and a
/// mechanism each service has to remember to switch on is one some service will forget —
/// silently, since nothing fails when an event is simply never sent.
/// </para>
/// <para>
/// Here rather than in the service package because the packages that need it cannot depend on
/// the service package without dragging ASP.NET into places that have no business with it, and
/// the service package cannot depend on them without dragging Entity Framework into services that
/// own no data. Both already depend on this one.
/// </para>
/// <para>
/// Runs after the service's own registrations, so an extension can see what the service
/// registered and act on it — and so a service that registers something first keeps it, which is
/// how a service opts out of a platform default.
/// </para>
/// </remarks>
public interface IServiceExtension
{
    /// <summary>Adds this package's contribution to a service.</summary>
    /// <param name="services">The service's registrations, including its own.</param>
    /// <param name="configuration">The service's configuration.</param>
    void Configure(IServiceCollection services, IConfiguration configuration);
}

/// <summary>
/// Declares that an assembly carries an <see cref="IServiceExtension"/> every service referencing
/// it should apply.
/// </summary>
/// <remarks>
/// Declared on the assembly rather than discovered by scanning for implementations, so the
/// platform loads one attribute per assembly rather than every type in it — and so an extension
/// is something a package author states on purpose, where a reviewer will see it.
/// </remarks>
/// <param name="extension">The extension's type. Must have a public parameterless constructor.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ServiceExtensionAttribute(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type extension)
    : Attribute
{
    /// <summary>The extension's type.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type Extension { get; } = extension;
}
