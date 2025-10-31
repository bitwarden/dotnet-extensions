# Bitwarden.Server.Sdk

The Bitwarden Server SDK is built for quickly getting started building
a Bitwarden-flavored service. The entrypoint for using it is adding `UseBitwardenSdk()`
on your web application and configuring MSBuild properties to configure the features you
want.

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
