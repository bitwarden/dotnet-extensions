# Bitwarden .NET Extensions

Shared .NET libraries and extensions for Bitwarden's server-oriented projects. This repository provides reusable SDK components, authentication, configuration management, feature flag systems, and more for .NET applications.

## Packages

### [Bitwarden.Core](extensions/Bitwarden.Core/)

Core authentication and JWT handling utilities for Bitwarden services.

-   `BitwardenIdentityClient` - Identity client for authentication
-   `BitwardenSecretsClient` - Secrets management client
-   `BitwardenEnvironment` - Environment configuration
-   `IdentityAuthenticatingHandler` - HTTP handler for authentication
-   Authentication models: `AccessTokenPayload`, `AuthenticationPayload`, `JwtToken`, `RefreshableAuthenticationPayload`

### [Bitwarden.Extensions.Configuration](extensions/Bitwarden.Extensions.Configuration/)

Configuration extensions for loading secrets.

-   `SecretsManagerConfigurationExtensions` - Extension methods for `IConfigurationBuilder`
-   `SecretsManagerConfigurationProvider` - Configuration provider implementation
-   `ParallelSecretLoader` - Parallel loading of secrets
-   `SecretsManagerConfigurationOptions` - Configuration options

### [Bitwarden.Server.Sdk](extensions/Bitwarden.Server.Sdk/)

Main SDK package (MSBuild SDK type) for quickly building Bitwarden-flavored services. Add `UseBitwardenSdk()` to your web application builder to get started.

**MSBuild Feature Flags:**

-   `<BitIncludeFeatures>true</BitIncludeFeatures>` - Enables feature flag support
-   Defines: `BIT_INCLUDE_FEATURES`, `BIT_INCLUDE_TELEMETRY`

### [Bitwarden.Server.Sdk.Features](extensions/Bitwarden.Server.Sdk.Features/)

Feature flag system with LaunchDarkly integration.

-   `IFeatureService` - Service for getting feature flag values
-   `LaunchDarklyFeatureService` - LaunchDarkly implementation
-   `RequireFeatureAttribute` - Controller/action attribute for requiring features
-   `FeatureEndpointConventionBuilderExtensions` - `.RequireFeature()` for minimal APIs
-   `FeatureCheckMiddleware` - Middleware for feature checks
-   `FeatureServiceCollectionExtensions` - DI extensions: `AddKnownFeatureFlags()`, `AddFeatureFlagValues()`
-   `FeatureApplicationBuilderExtensions` - `.UseFeatureFlagChecks()` middleware

### [Bitwarden.Server.Sdk.Authentication](extensions/Bitwarden.Server.Sdk.Authentication/)

Authentication middleware and JWT configuration for ASP.NET Core applications.

-   `AuthenticationServiceCollectionExtensions` - DI setup
-   `AuthenticationApplicationBuilderExtensions` - Middleware setup
-   `BitwardenConfigureJwtBearerOptions` - JWT Bearer configuration
-   `PostAuthenticationLoggingMiddleware` - Logging after authentication

## Getting Started

```bash
# Build the solution
dotnet build

# Run tests
dotnet test

# Pack a specific package
dotnet pack extensions/Bitwarden.Core/src/Bitwarden.Core.csproj
```

## Repository Structure

```
dotnet-extensions/
├── extensions/                          # All extension packages
│   ├── Bitwarden.Core/                 # Core library with JWT authentication
│   ├── Bitwarden.Extensions.Configuration/  # Configuration extensions
│   ├── Bitwarden.Server.Sdk/           # Main Server SDK (MSBuild SDK package)
│   └── Bitwarden.Server.Sdk.*/         # SDK components
├── docs/                               # Documentation
└── bitwarden-dotnet.sln               # Main solution file
```
