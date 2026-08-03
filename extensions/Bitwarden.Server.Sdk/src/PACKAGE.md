# Bitwarden.Server.Sdk

The Bitwarden Server SDK is built for quickly getting started building
a Bitwarden-flavored service. The entrypoint for using it is adding `UseBitwardenSdk()`
on your web application and configuring MSBuild properties to configure the features you
want.

The Bitwarden.Server.Sdk is consumed as a [MSBuild project SDK](https://learn.microsoft.com/en-us/visualstudio/msbuild/how-to-use-project-sdk?view=vs-2022)
but it is not intended to be consumed solely by itself and instead expected to be used alongside the
`Microsoft.NET.Sdk.Web` SDK. The most common way will be to import `<Sdk Name="Bitwarden.Server.Sdk" />`
right underneath the top level `<Project>` element in your `.csproj` file.

## Telemetry

Enabled by default for executable projects (`OutputType=Exe`) and disabled by default otherwise.
Can be overridden using `<BitIncludeTelemetry>true</BitIncludeTelemetry>` or
`<BitIncludeTelemetry>false</BitIncludeTelemetry>` in your project file.

This feature automatically includes a suite of OpenTelemetry libraries and registers those services
into the `IServiceCollection`.

## Features

Enabled by default and able to be removed using `<BitIncludeFeatures>false</BitIncludeFeatures>` in
your project file.

This feature automatically includes the `Bitwarden.Server.Sdk.Features` library and when using the
`Microsoft.NET.Sdk.Web` SDK it will register the services in `UseBitwardenSdk()`. All considerations
of that package apply to this SDK.

## Authentication

Enabled by default for executable projects (`OutputType=Exe`) and disabled by default otherwise.
Can be overridden using `<BitIncludeAuthentication>true</BitIncludeAuthentication>` or
`<BitIncludeAuthentication>false</BitIncludeAuthentication>` in your project file.

This feature automatically includes the `Bitwarden.Server.Sdk.Authentication` library and when using
the `Microsoft.NET.Sdk.Web` SDK it will register Bitwarden style authentication in
`UseBitwardenSdk()`. All considerations of that package apply to this SDK.

## Web Essentials

Enabled by default for executable projects (`OutputType=Exe`) and disabled by default otherwise.
Can be overridden using `<BitIncludeWebEssentials>true</BitIncludeWebEssentials>` or
`<BitIncludeWebEssentials>false</BitIncludeWebEssentials>` in your project file.

This feature automatically includes the `Bitwarden.Server.Sdk.WebEssentials` library and registers
its services in `UseBitwardenSdk()`. All considerations of that package apply to this SDK.

To add the security headers middleware, call `UseSecurityHeaders()` on your application builder:

```csharp
app.UseSecurityHeaders();
```

## Environment

Enabled by default for all project types. Can be disabled using
`<BitIncludeEnvironment>false</BitIncludeEnvironment>` in your project file.

This feature automatically includes the `Bitwarden.Server.Sdk.Environment` library and registers
`IBitwardenEnvironment` in `UseBitwardenSdk()`. `IBitwardenEnvironment` exposes the application
version, Git hash, and self-host details.

Note that `Bitwarden.Server.Sdk.Features` and `Bitwarden.Server.Sdk.WebEssentials` both depend on
this package internally, so disabling `BitIncludeEnvironment` while either of those is enabled will
produce a build warning.

If your service can run in a self-hosted configuration, configure `SelfHostDetails` after calling
`UseBitwardenSdk()`:

```csharp
using Bitwarden.Server.Sdk.Environment.Setup;

builder.Services.AddOptions<SelfHostDetails>()
    .Configure<IConfiguration>((details, config) =>
    {
        if (config.GetValue<bool>("globalSettings:selfHosted"))
            details.MakeSelfHost(config["globalSettings:selfHostFlavor"] ?? "unknown");
        else
            details.MakeCloud();
    });
```

## Deployment Files

The SDK provides two MSBuild targets for producing a list of every file whose change should
trigger a new deployment of the project — its source files, resources, content, referenced
project files, and NuGet lock file. This list is useful in CI to decide whether a PR actually
needs a release.

### Targets

**`ListDeploymentFiles`** prints one absolute path per line to the MSBuild console:

```
dotnet msbuild -t:ListDeploymentFiles -v:m
```

**`WriteDeploymentFiles`** writes the same list to disk so CI scripts can consume it without
parsing build output. The default output path is `obj/deployment-files.txt` and can be
overridden:

```
dotnet msbuild -t:WriteDeploymentFiles
dotnet msbuild -t:WriteDeploymentFiles -p:DeploymentFilesOutputPath=deployment-files.txt
```

### What is included

The following are included automatically:

- The `.csproj` file of each project
- All `@(Compile)` items (`.cs` source files)
- All `@(EmbeddedResource)` items
- All `@(Content)` items where `CopyToOutputDirectory` is not `Never`
- `packages.lock.json` when `RestorePackagesWithLockFile` is `true`
- All of the above from transitively referenced projects

To manually include additional files — such as SQL migration scripts, Dockerfiles, or
configuration templates — add them to the `BitDeploymentInput` item group:

```xml
<ItemGroup>
  <BitDeploymentInput Include="Dockerfile" />
  <BitDeploymentInput Include="migrations/**/*.sql" />
</ItemGroup>
```

### CI usage

The following GitHub Actions job uses `WriteDeploymentFiles` to gate a release on whether
any deployment-relevant file was changed in the PR:

```yaml
jobs:
  check-deployment:
    runs-on: ubuntu-latest
    outputs:
      needs-release: ${{ steps.check.outputs.needs-release }}
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - uses: actions/setup-dotnet@v4

      - name: Write deployment files
        run: |
          dotnet msbuild src/MyService/MyService.csproj \
            -t:WriteDeploymentFiles \
            -p:DeploymentFilesOutputPath=deployment-files.txt \
            --nologo -v:q

      - name: Check for overlap with PR changes
        id: check
        run: |
          git diff --name-only origin/${{ github.base_ref }}...HEAD \
            | sed "s|^|$GITHUB_WORKSPACE/|" \
            > changed-files.txt

          if grep -qxFf deployment-files.txt changed-files.txt; then
            echo "needs-release=true" >> $GITHUB_OUTPUT
          else
            echo "needs-release=false" >> $GITHUB_OUTPUT
          fi
```

The `sed` step converts `git diff`'s repo-relative paths to absolute paths so they can be
compared against the absolute paths written by `WriteDeploymentFiles`. `grep -xFf` does
exact whole-line fixed-string matching, so partial path matches cannot produce false
positives.

## Aspire Integration

Disabled by default and able to be enabled using `<BitAspireIntegration>enabled</BitAspireIntegration>`
in your project file.

This feature adds service discovery support via `Microsoft.Extensions.ServiceDiscovery`, registering
it into the `IServiceCollection` and configuring it as the default for all `HttpClient` instances.
When telemetry is also enabled, OTLP log exporting is configured with formatted messages and scopes
included.

This is primarily useful for local development with .NET Aspire. To enable it only in debug builds:

```xml
<BitAspireIntegration Condition="'$(Configuration)' == 'Debug'">enabled</BitAspireIntegration>
```
