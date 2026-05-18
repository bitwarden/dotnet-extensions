using System.Diagnostics.CodeAnalysis;
using Azure.Storage.Blobs;
using Bitwarden.Server.Sdk.Licensing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering the Bitwarden licensing services on an <see cref="IServiceCollection"/>.
/// </summary>
public static class LicensingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the licensing services, including <see cref="ILicenseGenerator{T}"/>,
    /// <see cref="ILicenseReader{T}"/>, and the certificate providers used to sign and validate
    /// licenses.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="staticLicensingOptions">Static, startup-time licensing configuration (issuer, audience, thumbprints).</param>
    /// <returns>The supplied <paramref name="services"/>, to allow chaining.</returns>
    public static IServiceCollection AddLicensing(this IServiceCollection services, StaticLicensingOptions staticLicensingOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(staticLicensingOptions);

        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton(staticLicensingOptions);

        // TODO: Should IIssuerCertificateProvider unconditionally throw on self-host
        services.TryAddSingleton<IIssuerCertificateProvider, IssuerCertificateProvider>();

        services.AddOptions();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<LicensingOptions>, ConfigureLicensingOptions>());

        services.TryAddSingleton<IOptionsFactory<BlobClientOptions>, BlobClientOptionsFactory>();

        services.TryAddSingleton<ISigningCertificateProvider>(static sp =>
        {
            var licensingOptions = sp.GetRequiredService<IOptions<LicensingOptions>>().Value;
            var hostEnvironment = sp.GetRequiredService<IHostEnvironment>();
            var staticLicensingOptions = sp.GetRequiredService<StaticLicensingOptions>();

            if (!string.IsNullOrEmpty(licensingOptions.Azure.ConnectionString))
            {
                var blobClientOptions = sp.GetRequiredService<IOptionsMonitor<BlobClientOptions>>().Get("Licensing");

                return new AzureBlobSigningCertificateProvider(
                    licensingOptions.Azure.ConnectionString,
                    licensingOptions.Azure.FileName,
                    licensingOptions.Azure.ContainerName,
                    licensingOptions.CertificatePassword,
                    blobClientOptions
                );
            }
            // Development allows loading the certificate from the users cert store.
            else if (hostEnvironment.IsDevelopment()
                && SigningCertificateProvider.TryGetFromCertificateStore(
                    staticLicensingOptions.GetThumbprint(hostEnvironment.EnvironmentName),
                    licensingOptions.CertificatePassword,
                    out var certificate))
            {
                return new SigningCertificateProvider(certificate);
            }

            return new NotSupportedSigningCertificateProvider();
        });


        // Hosted service that retrieves the signing certificate once on startup to avoid the first caller
        // possibly forcing async-over-sync.
        services.AddHostedService<SigningCertificateActivator>();

        // TODO: Use correct implementation whether or not we have the private key
        services.TryAdd(ServiceDescriptor.Singleton(typeof(ILicenseGenerator<>), typeof(LicenseGenerator<>)));
        services.TryAdd(ServiceDescriptor.Singleton(typeof(ILicenseReader<>), typeof(LicenseReader<>)));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<TokenValidationParameters>, ConfigureTokenValidationParametersOptions>());

        return services;
    }

    /// <summary>
    /// Registers a claims factory that contributes claims to licenses issued for items of type
    /// <typeparamref name="T"/>. Multiple factories may be registered for the same
    /// <typeparamref name="T"/>; each is invoked in registration order during license generation.
    /// </summary>
    /// <typeparam name="T">The type of item the license is issued for.</typeparam>
    /// <typeparam name="TImplementation">The concrete claims factory implementation.</typeparam>
    /// <param name="services">The service collection to register against.</param>
    /// <returns>The supplied <paramref name="services"/>, to allow chaining.</returns>
    public static IServiceCollection AddLicenseFactory<
        T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation
    >
        (this IServiceCollection services)
        where TImplementation : class, ILicenseClaimsFactory<T>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILicenseClaimsFactory<T>, TImplementation>());

        return services;
    }
}

internal sealed class BlobClientOptionsFactory : OptionsFactory<BlobClientOptions>
{
    public BlobClientOptionsFactory(
        IEnumerable<IConfigureOptions<BlobClientOptions>> setups,
        IEnumerable<IPostConfigureOptions<BlobClientOptions>> postConfigures,
        IEnumerable<IValidateOptions<BlobClientOptions>> validations)
        : base(setups, postConfigures, validations)
    {
    }

    protected override BlobClientOptions CreateInstance(string name)
    {
        return new BlobClientOptions();
    }
}
