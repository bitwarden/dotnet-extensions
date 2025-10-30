# Server SDK Feature Flags

Feature flag system with LaunchDarkly integration.

- `IFeatureService` - Service for getting feature flag values
- `LaunchDarklyFeatureService` - LaunchDarkly implementation
- `RequireFeatureAttribute` - Controller/action attribute for requiring features
- `FeatureEndpointConventionBuilderExtensions` - `.RequireFeature()` for minimal APIs
- `FeatureCheckMiddleware` - Middleware for feature checks
- `FeatureServiceCollectionExtensions` - DI extensions: `AddKnownFeatureFlags()`, `AddFeatureFlagValues()`
- `FeatureApplicationBuilderExtensions` - `.UseFeatureFlagChecks()` middleware
- `IContextBuilder` extension point for consumers to customize the context that is used when evaluating feature flag values
- `FeatureCheckOptions` options object used for customizing the behavior of feature checks
