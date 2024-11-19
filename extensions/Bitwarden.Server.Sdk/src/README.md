# Bitwarden.Server.Sdk

The Bitwarden Server SDK is built for quickly getting started building
a Bitwarden-flavored service. The entrypoint for using it is adding `UseBitwardenSdk()`
on your web application and configuring MSBuild properties to configure the features you
want.

## Feature Flags

Feature flag support can be added by adding `<BitIncludeFeatures>true</BitIncludeFeatures>` to
your `csproj` file. The following APIs become available:

- `IFeatureService` for getting values of features.
- `RequireFeatureAttribute` for requiring a feature is enabled on controllers and controller actions.
- `IEndpointConventionBuilder.RequireFeature()` for requiring a feature is enabled on minimal APIs.
- `IApplicationBuilder.UseFeatureFlagChecks()` for adding the middleware to do the above checks.
- `IServiceCollection.AddKnownFeatureFlags()` for adding flags that will show up in `IFeatureService.GetAll()`
- `IServiceCollection.AddFeatureFlagValues()` for adding values for feature flags.
