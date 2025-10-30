# Bitwarden.Server.Sdk

The Bitwarden Server SDK is built for quickly getting started building
a Bitwarden-flavored service. The entrypoint for using it is adding `UseBitwardenSdk()`
on your web application and configuring MSBuild properties to configure the features you
want.

The Bitwarden.Server.Sdk is consumed as a [MSBuild project SDK][msbuild-project-sdk] but it is not
intended to be consumed soley by itself and instead expected to be used alongside the
`Microsoft.NET.Sdk.Web` SDK. The most common way will be to import `<Sdk Name="Bitwarden.Server.Sdk" />`
right underneath the top level

## Telemetry

Enabled by default and able to be removed using `<BitIncludeTelemetry>false</BitIncludeTelemetry>`
in your project file.

This feature automatically includes a suite of OpenTelemetry libraries and registers those services
into the `IServiceCollection`.

## Features

Enabled by default and able to be removed using `<BitIncludeFeatures>false</BitIncludeFeatures>` in
your project file.

This feature automatically includes the `Bitwarden.Server.Sdk.Features` library and when using the
`Microsoft.NET.Sdk.Web` SDK it will register the services in `UseBitwardenSdk()`. All considerations
of that package apply to this SDK.

## Authentication

Enabled by default and able to be removed using
`<BitIncludeAuthentication>false</BitIncludeAuthentication>` in your project file.

This feature automatically includes the `Bitwarden.Server.Sdk.Authentication` library and when using
the `Microsoft.NET.Sdk.Web` SDK it will register Bitwarden style authentication in
`UseBitwardenSdk()`. All considerations of that package apply to this SDK.

[msbuild-project-sdk]:[https://learn.microsoft.com/en-us/visualstudio/msbuild/how-to-use-project-sdk?view=vs-2022]
