using LaunchDarkly.Sdk;

namespace Bitwarden.Server.Sdk.Features;

/// <summary>
/// A service for customizing the building of <see cref="Context"/> for your application.
/// </summary>
/// <remarks>
/// <para>
/// This service will be registered as a <see cref="Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton"/>.
/// </para>
/// <para>
/// To customize specifically for an HTTP call it's recommended to use <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/>
/// and access the <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor.HttpContext"/> property. If that value is
/// not <see langword="null" /> then you can use <see cref="Microsoft.AspNetCore.Http.HttpContext.RequestServices"/> to
/// obtain request scoped services that can be used to build your context. If that value is <see langword="null"/> then
/// the feature flag check is not happening during the context of an HTTP call. It is likely that it's instead taking
/// place in a <see cref="Microsoft.Extensions.Hosting.IHostedService"/>.
/// </para>
/// <para>
/// Because this service is a singleton, implementations should be stateless. Do not cache the built
/// <see cref="Context"/> in an instance field, doing so would share one request's context with every other request.
/// If building your context is expensive, store the result in
/// <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/> so that it is cached only for the current request.
/// </para>
/// </remarks>
public interface IContextBuilder
{
    /// <summary>
    /// Called every time a feature flag value is requested. The returned value is not cached, if building your
    /// context is expensive it is your responsibility to cache it. See the remarks on <see cref="IContextBuilder"/>
    /// for how to do that safely from a singleton.
    /// </summary>
    /// <returns>The Context to use for the current feature flag request.</returns>
    public Context Build();
}
