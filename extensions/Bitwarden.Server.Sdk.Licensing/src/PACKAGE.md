# Bitwarden.Server.Sdk.Licensing

## About

This package generates and validates signed JWT licenses. Licenses are signed with an X.509
certificate's private key and validated against its public counterpart. Consumers describe the
shape of their license by registering one or more `ILicenseClaimsFactory<T>` implementations that
contribute claims for items of type `T`.

## How to use

Register licensing services and at least one claims factory for each item type you want to issue
licenses for:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLicensing(new StaticLicensingOptions
{
    Issuer = "bitwarden",
    Audience = "my-product",
    EnvironmentThumbprints = new Dictionary<string, string>
    {
        ["Production"] = "A1B2C3...",
    },
    FallbackThumbprint = "A1B2C3...",
});

builder.Services.AddLicenseFactory<User, UserLicenseClaimsFactory>();
```

A claims factory contributes the claims for one item:

```csharp
public sealed class UserLicenseClaimsFactory : ILicenseClaimsFactory<User>
{
    public ValueTask AddClaimsAsync(LicenseClaimsContext context, User user, CancellationToken cancellationToken)
    {
        context.AddClaim("sub", user.Id.ToString());
        context.AddClaim("email", user.Email);
        return ValueTask.CompletedTask;
    }
}
```

Generate and read a license:

```csharp
public sealed class LicensingController(
    ILicenseGenerator<User> generator,
    ILicenseReader<User> reader)
{
    public async Task<string> Issue(User user)
        => await generator.GenerateAsync(user, DateTimeOffset.UtcNow.AddYears(1));

    public async Task<IReadOnlyDictionary<string, string>> Read(string token)
        => await reader.ReadLicenseAsync(token);
}
```

## Signing certificate resolution

`AddLicensing` resolves the signing certificate using the first strategy that matches:

1. **Azure Blob Storage** - if `Licensing:Azure:ConnectionString` is set, the certificate is
   downloaded from the configured container and blob name.
2. **Local certificate store** (Development only) - the certificate is looked up in the current
   user's `My` store by the thumbprint configured for the current environment.
3. **Not supported** - if neither path applies, signing is unavailable for the process. License
   validation may still work if the issuer's public certificate is embedded (see below).

Example JSON configuration for the Azure Blob Storage path:

```json
{
  "Licensing": {
    "CertificatePassword": "...",
    "Azure": {
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...",
      "ContainerName": "certificates",
      "FileName": "licensing.pfx"
    }
  }
}
```

`CertificatePassword` is used to unlock the private key regardless of which load strategy is
selected.

## Validating licenses

`ILicenseReader<T>` validates the JWT against the public certificate returned by
`IIssuerCertificateProvider`. The default implementation loads the public certificate from an
embedded resource on the application's entry assembly. To use it, embed each public certificate
(the `.cer`, not the `.pfx`) under a `Licensing` folder in your application project so its manifest
resource name is `{AssemblyName}.Licensing.{thumbprint}.cer`, e.g.:

```xml
<ItemGroup>
  <EmbeddedResource Include="Licensing\*.cer" />
</ItemGroup>
```

The thumbprint used for the lookup follows the same per-environment rules as
`StaticLicensingOptions`: an entry in `EnvironmentThumbprints` for the current environment takes
precedence, otherwise `FallbackThumbprint` is used.
