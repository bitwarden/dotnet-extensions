# Server SDK Feature Flags

Feature flag system with LaunchDarkly integration.

- `IFeatureService` - Service for getting feature flag values
- `LaunchDarklyFeatureService` - LaunchDarkly implementation
- `RequireFeatureAttribute` - Controller/action attribute for requiring features
- `FeatureEndpointConventionBuilderExtensions` - `.RequireFeature()` for minimal APIs
- `FeatureCheckMiddleware` - Middleware for feature checks
- `FeatureServiceCollectionExtensions` - DI extensions: `AddKnownFeatureFlags()`, `AddFeatureFlagValues()`
- `FeatureApplicationBuilderExtensions` - `.UseFeatureFlagChecks()` middleware
