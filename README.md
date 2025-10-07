# Bitwarden .NET Extensions

Shared .NET libraries and extensions for Bitwarden's server-oriented projects. This repository provides reusable SDK components, authentication, configuration management, feature flag systems, and more for .NET applications.

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
│   ├── Bitwarden.Core/                 # Library containing basic authentication and crypto to get the .NET configuration provider working
│   ├── Bitwarden.Extensions.Configuration/  # .NET Configuration provider using Secrets Manager
│   ├── Bitwarden.Server.Sdk/           # Main Server SDK (MSBuild SDK package)
│   └── Bitwarden.Server.Sdk.*/         # SDK components
├── docs/                               # Documentation
└── bitwarden-dotnet.sln               # Main solution file
```
