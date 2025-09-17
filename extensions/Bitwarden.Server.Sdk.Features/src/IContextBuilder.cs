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
/// </remarks>
public interface IContextBuilder
{
    /// <summary>
    /// Called the first time a feature flag value is requested in each service scope. The returned value is cached
    /// for all subsequent feature flag requests.
    /// </summary>
    /// <returns>The Context to use for all feature flag requests in this service scope.</returns>
    public Context Build();
}
