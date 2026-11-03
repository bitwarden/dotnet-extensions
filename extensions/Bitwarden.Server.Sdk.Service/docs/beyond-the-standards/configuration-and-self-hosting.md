# Configuration and self-hosting

A service reads its settings from the standard ASP.NET Core sources. Every setting the package reads
is optional: a service with none of them runs in the cloud configuration. One setting,
`globalSettings:selfHosted`, switches a service into a self-hosted install's behaviour.

## What the package does

- **Standard sources.** `appsettings.json`, `appsettings.{Environment}.json`, user secrets in
  Development, environment variables and the command line, through `WebApplication.CreateBuilder`.
  Sources you add in `ConfigureConfiguration` come last and win.
- **Defaults it supplies** as the lowest-precedence source, so a deployment can override them:
  `Bitwarden:ServiceName`, `Logging:Console:IncludeScopes` and the request-line log level.
- **Self-hosted installs.** With `globalSettings:selfHosted: true`:
    - `appsettings.SelfHosted.json`, when present, is layered in right after
      `appsettings.{Environment}.json`, so it overrides the service's own settings and is overridden
      by user secrets, environment variables and the command line;
    - `IBitwardenEnvironment.SelfHosted` is true and `SelfHostFlavor` is
      `globalSettings:selfHostFlavor` (`unknown` when unset);
    - per-address rate limits are off, telemetry exporters default to off, and
      `AddCloudSsrfProtection()` clients are unguarded;
    - the config resource reports `settings.selfHosted: true`.
- **`[SelfHostedOnly]` and `[NotSelfHostedOnly]`** on a handler or its class refuse the operation
  `422` in the other kind of install. The check runs after the request is bound and validated.
- **Feature flags.** Every service gets `IFeatureService` (Bitwarden.Server.Sdk.Features):
  LaunchDarkly when `Features:LaunchDarkly:SdkKey` is set, otherwise the offline values in
  `Features:FlagValues`.
- **`[RequireFeature("key")]`** on a handler or its class answers `404` while the flag is off. The
  check runs after authentication and authorization, so a caller the operation would refuse is
  refused `401` or `403` whatever the flag, and only an authorized caller learns that it is off.
- **`GET /api/v1/config`**, for a product service only: anonymous, and what a client reads before
  and after sign-in.

## Example

Hold an operation behind a flag, read a flag inside one, and keep a resource to self-hosted
installs:

```csharp
using Bitwarden.Server.Sdk.Environment;
using Bitwarden.Server.Sdk.Features;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup("api/v1/widgets").WithStandards();
        widgets.MapPost("{id}/actions/polish", Polish);
        widgets.MapPost("{id}/actions/share", Share);
    }

    /// <summary>
    /// Polish a widget
    /// </summary>
    // Answers 404 to an authorized caller while the flag is off.
    [RequireFeature("pm-12345-widget-polish")]
    [StandardAction]
    public static Task Polish(string id, IWidgetStore store, CancellationToken cancellationToken) =>
        store.PolishAsync(id, cancellationToken);

    /// <summary>
    /// Share a widget
    /// </summary>
    [StandardAction]
    public static async Task<ResourceDocument<Widget>> Share(
        string id, IFeatureService features, IWidgetStore store, CancellationToken cancellationToken)
    {
        // A flag that changes how an operation behaves, rather than whether it exists.
        var notify = features.IsEnabled("pm-23456-widget-share-notifications");
        return ActionResponse(await store.ShareAsync(id, notify, cancellationToken));
    }
}

// Every operation in the class is refused 422 in Bitwarden's cloud.
[ApiTag(Description = "The license a self-hosted install's widgets run under.")]
[SelfHostedOnly]
public static class WidgetLicensesEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var licenses = endpoints.MapGroup("api/v1/widget-licenses").WithStandards();
        licenses.MapPost("current/actions/refresh", Refresh);
    }

    /// <summary>
    /// Refresh the install's widget license
    /// </summary>
    [StandardAction("refresh", template: "current")]
    public static Task Refresh(IBitwardenEnvironment environment, IWidgetStore store, CancellationToken cancellationToken) =>
        store.RefreshLicenseAsync(environment.SelfHostFlavor, cancellationToken);
}
```

In the cloud, `Refresh` answers:

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"422","code":"invalid-value","title":"A field has an invalid value.","detail":"Only allowed when self hosted."}]}
```

A `[NotSelfHostedOnly]` operation in a self-hosted install answers the same with "Only allowed when
not self hosted." With its flag off, `Polish` answers exactly as an address that matches nothing:

```http
HTTP/1.1 404 Not Found
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"404","code":"not-found","title":"The resource does not exist.","detail":"No resource exists at this address."}]}
```

### The config resource

A product service publishes `GET /api/v1/config`. Anyone may read it; a valid session token, when
sent, has the flags evaluated for the person, and one that fails validation gets the anonymous
answer.

```json
{
    "data": {
        "type": "config",
        "attributes": {
            "environment": {
                "cloudRegion": "US",
                "vault": "https://vault.example.com",
                "fillAssistRules": "https://example.com/fill-assist-rules.json"
            },
            "settings": {
                "disableUserRegistration": false,
                "suppressOnboardingInterstitials": false,
                "selfHosted": false
            },
            "featureFlags": {
                "pm-12345-widget-polish": true
            },
            "version": "1.0.0"
        }
    }
}
```

- **`version`** is `IBitwardenEnvironment.Version`, or the service assembly's informational version
  without build metadata.
- **`featureFlags`** is `IFeatureService.GetAll()`: only the flags listed in `Features:KnownFlags`.
- An `environment` member whose setting is unset is omitted.

The flags are evaluated for a LaunchDarkly context the platform builds from the request: a `user`
context keyed by the acting user (or a shared anonymous key), with the person's confirmed
organizations; a `device` context from the token's `device` claim or the `Device-Identifier` header;
and the client's version and type. With an SDK key that context goes to LaunchDarkly: identifiers,
client version and type, never Vault Data. Register your own builder with
`services.AddContextBuilder<T>()` in `ConfigureServices`.

## Configuration

| Setting                                            | Default   | What it does                                                                          |
| -------------------------------------------------- | --------- | ------------------------------------------------------------------------------------- |
| `globalSettings:selfHosted`                        | `false`   | Marks a self-hosted install, with the effects listed above.                           |
| `globalSettings:selfHostFlavor`                    | `unknown` | The self-hosted install's flavor, read only when `globalSettings:selfHosted` is true. |
| `Features:LaunchDarkly:SdkKey`                     | None      | Evaluates flags with LaunchDarkly. A secret.                                          |
| `Features:FlagValues`                              | None      | Flag values served offline when there is no SDK key.                                  |
| `Features:KnownFlags`                              | None      | The flags `GetAll()`, and so the config resource, report.                             |
| `Service:Environment:CloudRegion`                  | None      | The config resource's `environment.cloudRegion`.                                      |
| `Service:Environment:Vault`                        | None      | The config resource's `environment.vault`, the web vault's address.                   |
| `Service:Environment:FillAssistRules`              | None      | The config resource's `environment.fillAssistRules`.                                  |
| `Service:Settings:DisableUserRegistration`         | `false`   | The config resource's `settings.disableUserRegistration`.                             |
| `Service:Settings:SuppressOnboardingInterstitials` | `false`   | The config resource's `settings.suppressOnboardingInterstitials`.                     |

The other settings the package reads are on the page for their topic.

## What you decide

- **Secrets stay out of `appsettings*.json`.** The LaunchDarkly SDK key, Redis connection strings
  and database passwords belong in user secrets or the environment.
- **Flag naming and clean-up.** The package evaluates flags; it does not track which ones are stale.
  Remove `[RequireFeature]` and the flag from `Features:KnownFlags` once a feature has shipped.
- **Self-hosted settings.** Ship an `appsettings.SelfHosted.json` for what differs in a self-hosted
  install, rather than branching on `IBitwardenEnvironment.SelfHosted` in code.
- **Operations outside the standards.** `[SelfHostedOnly]` and `[NotSelfHostedOnly]` are enforced on
  endpoints mapped under `WithStandards()`. Elsewhere, check `IBitwardenEnvironment.SelfHosted`
  yourself.

## See also

- [Defining a service](defining-a-service.md)
- [Network and request limits](network-and-request-limits.md)
- [Observability and health](observability-and-health.md)
- [Outbound HTTP](outbound-http.md)
- [API standards](https://contributing.bitwarden.com/architecture/server/apis)
