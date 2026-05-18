using System.Diagnostics.CodeAnalysis;
using Azure.Storage.Blobs;
using Bitwarden.Server.Sdk.Licensing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///
/// </summary>
public static class LicensingServiceCollectionExtensions
{
    /// <summary>
    ///
    /// </summary>
    /// <param name="services"></param>
    /// <param name="staticLicensingOptions"></param>
    /// <returns></returns>
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
    ///
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="TImplementation"></typeparam>
    /// <param name="services"></param>
    /// <returns></returns>
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
