# Server SDK Licensing

JWT-based license generation and validation backed by an X.509 signing certificate.

- `ILicenseGenerator<T>` / `LicenseGenerator<T>` - Generate signed JWT licenses for items of type `T`
- `ILicenseReader<T>` / `LicenseReader<T>` - Validate and read claims out of a license
- `ILicenseClaimsFactory<T>` - Extension point for contributing claims to a license being generated
- `LicenseClaimsContext` - Accumulator passed to each claims factory during generation
- `ISigningCertificateProvider` / `SigningCertificateProvider` - Source of the private-key certificate used for signing (Azure Blob, local cert store, or not-supported)
- `IIssuerCertificateProvider` - Source of the public certificate used for validation (loaded from embedded `.cer` resources)
- `LicensingServiceCollectionExtensions` - DI extensions: `AddLicensing()`, `AddLicenseFactory<T, TImpl>()`
- `StaticLicensingOptions` - Startup-time configuration (issuer, audience, thumbprints)
- `LicensingOptions` / `AzureBlobLicensingOptions` - Configuration bound from the `Licensing` section of `IConfiguration`
