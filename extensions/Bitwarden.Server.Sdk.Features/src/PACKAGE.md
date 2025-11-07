# Bitwarden.Server.Sdk.Features

## About

This package enables the use of feature flags to allow remotely toggling features. You can read more about
feature management at Bitwarden by reading
[this ADR](https://contributing.bitwarden.com/architecture/adr/feature-management).

## How to use

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFeatureFlagServices();

var app = builder.Build();

app.UseRouting();

app.UseFeatureFlagChecks();

app.MapGet("/", () =>
{
    return Results.Ok("my-feature enabled!");
})
    .RequireFeature("my-feature");

app.Run();
```

```csharp
class MyService(IFeatureService featureService)
{
    public void Run()
    {
        if (featureService.IsEnabled("my-feature"))
        {
            RunNew();
        }
        else
        {
            RunOld();
        }
    }
}
```

The `[RequireFeature]` attribute can also be placed on a controller class or individual controller
actions.

Both the `RequireFeature` method on minimal API endpoints and the `[RequireFeature]` attribute
require the `UseFeatureFlagChecks()` middleware to be registered. It needs to be after `UseRouting`
and before `UseEndpoints`/`Map*`. If you rely on authentication information in your custom
[context builder](#context-builder) then the middleware should also be registered after your
authentication middleware.

Out of the box, this package binds our options object `FeatureFlagOptions` to the `Features` section
of configuration. This means to enable the use of Launch Darkly you need to have
`Features:LaunchDarkly:SdkKey` set. If no key is set then local flag values can be used through
`Features:FlagValues:<Key>=<Value>`. This can be helpful for local development.

`Features:KnownFlags` must be populated with all flag keys that you wish to be returned from
`IFeatureService.GetAll()`. If you have no need to use that method you do not need to add values to
that option. `IFeatureService.IsEnabled` and other single feature checks should continue to work
just fine without it.

Example JSON configuration:

```json
{
  "Features": {
    "FlagValues": {
      "my-flag": true,
      "another-flag": false
    },
    "KnownFlags": ["my-flag"],
    "LaunchDarkly": {
      "SdkKey": "your-sdk-key"
    }
  }
}
```

## Customization

### Context Builder

By default the feature flag context will be for an anonymous user. This doesn't allow granular
targeting of feature flag values. To enable this you can implement your own `IContextBuilder` and
register it using `services.AddContextBuilder<MyContextBuilder>()`. Learn more about context
configuration by reading the code docs on `IContextBuilder` and reading Launch Darkly's docs on
[context configuration](https://launchdarkly.com/docs/sdk/features/context-config#expand-net-server-side-code-sample).

### OnFeatureCheckFailed

The response that is sent back to the client on failed feature checks will return a problem details
formatted error with a 404 status code by default but this can be customized through the
`OnFeatureCheckFailed` property on `FeatureCheckOptions`.
