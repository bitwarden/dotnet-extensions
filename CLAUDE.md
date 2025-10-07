# Bitwarden .NET Extensions - Claude Instructions

## Repository Overview

This is the **Bitwarden .NET Extensions** repository, containing shared .NET libraries and extensions for Bitwarden's server-oriented projects. The repository provides reusable SDK components, authentication, configuration management, feature flag systems, and more for .NET applications.

## Package Structure Convention

Each package follows a consistent folder structure:

-   `extensions/[PackageName]/src/` - Main source code and .csproj file
-   `extensions/[PackageName]/tests/` - Unit/integration tests
-   `extensions/[PackageName]/examples/` - Example projects (optional)
-   `extensions/[PackageName]/perf/` - Performance benchmarks (optional)

## Key Packages

See the [README.md](README.md) for detailed information about each package. Main packages include:

-   **Bitwarden.Core** - Core authentication and JWT handling
-   **Bitwarden.Extensions.Configuration** - Configuration integration
-   **Bitwarden.Server.Sdk** - Main SDK for building Bitwarden services

## .NET and Build Configuration

### Target Framework

-   **NET 8.0** (`net8.0`) for all projects
-   SDK Version: 8.0.100 with `latestFeature` roll-forward policy

### Common Project Properties

```xml
<ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable>
<GenerateDocumentationFile>true</GenerateDocumentationFile>
<PublishRepositoryUrl>true</PublishRepositoryUrl>
<EmbedUntrackedSources>true</EmbedUntrackedSources>
<DebugType>embedded</DebugType>
```

### Package Metadata (for packable projects)

```xml
<Authors>Bitwarden</Authors>
<PackageProjectUrl>https://github.com/bitwarden/dotnet-extensions</PackageProjectUrl>
<PackageReleaseNotes>https://github.com/bitwarden/dotnet-extensions/releases</PackageReleaseNotes>
<PackageLicenseExpression>GPL-3.0-only</PackageLicenseExpression>
<PackageReadmeFile>README.md</PackageReadmeFile>
```

### Versioning Convention

```xml
<VersionPrefix>X.Y.Z</VersionPrefix>
<PreReleaseVersionLabel>beta</PreReleaseVersionLabel>
<PreReleaseVersionIteration>N</PreReleaseVersionIteration>
<VersionSuffix Condition="'$(VersionSuffix)' == '' AND '$(IsPreRelease)' == 'true'">$(PreReleaseVersionLabel).$(PreReleaseVersionIteration)</VersionSuffix>
```

## Testing Standards

### Test Framework

-   **xUnit**
-   `Microsoft.NET.Test.Sdk`
-   `coverlet.collector` for code coverage

### Additional Test Tools

-   `xunit.runner.visualstudio`
-   `NSubstitute` for mocking
-   `Microsoft.AspNetCore.TestHost` for integration tests
-   `Testcontainers` for container-based tests
-   `MSBuild.ProjectCreation` for MSBuild testing

### Test Project Structure

-   Test projects are in `tests/` subfolder of each extension
-   Test projects use exact version pinning: `Version="[X.Y.Z]"`
-   `<IsPackable>false</IsPackable>` and `<IsTestProject>true</IsTestProject>` properties
-   `<Using Include="Xunit" />` for global xUnit usings

## Code Style and Conventions

### C# Language Features

-   **Implicit usings enabled** - Common namespaces automatically included
-   **Nullable reference types enabled** - All projects use `<Nullable>enable</Nullable>`
-   File-scoped namespaces preferred (modern C# style)

### Dependency Version Pinning

-   Use **exact version matching** with brackets: `Version="[X.Y.Z]"`
-   This ensures consistent builds and no unexpected version changes
-   Example: `<PackageReference Include="xunit" Version="[2.9.3]" />`

### Project References

-   Use relative paths for project references within the solution
-   Example: `<ProjectReference Include="..\..\Bitwarden.Core\src\Bitwarden.Core.csproj" />`

### Naming Conventions

-   Packages prefixed with `Bitwarden.` (e.g., `Bitwarden.Core`, `Bitwarden.Server.Sdk`)
-   Test projects suffixed with `.Tests` or `.IntegrationTests`
-   Example projects in `examples/` subfolder
-   Performance benchmarks suffixed with `.Microbenchmarks`

## Release Process

### Branch Strategy

-   Main development on `main` branch
-   Release branches: `release/[Package]/X.Y` (major.minor only)
-   Version bumps happen automatically via workflows

### Workflow Files

-   `.github/workflows/start-release.yml` - Start a new release
-   `.github/workflows/prerelease.yml` - Create prereleases (alpha, beta, rc)
-   `.github/workflows/release.yml` - Full release to NuGet

### Version Bumping

-   **Automatic**: Minor and patch versions via workflows
-   **Manual**: Major versions and prerelease label changes (alpha → beta → rc)
-   Prerelease format: `X.Y.Z-[label].[iteration]` (e.g., `1.1.0-beta.2`)

### New Package Setup

1. Create folder: `extensions/[PackageId]/src/`
2. Add versioning properties to `.csproj`
3. Add package ID to `start-release.yml` workflow inputs
4. Include README.md in package

## Development Guidelines

### When Adding New Features

1. **Add XML documentation**: All public APIs require XML docs (`<GenerateDocumentationFile>true</GenerateDocumentationFile>`)
2. **Write tests**: Unit tests required, integration tests for complex features
3. **Update README**: Each package has its own README.md

### When Modifying Authentication

-   Changes likely affect `Bitwarden.Core` or `Bitwarden.Server.Sdk.Authentication`
-   JWT handling uses `System.IdentityModel.Tokens.Jwt`
-   Authentication payloads and tokens defined in `Bitwarden.Core`

### When Working with Configuration

-   Secrets integration in `Bitwarden.Extensions.Configuration`
-   Parallel loading supported via `ParallelSecretLoader`
-   Custom configuration sources follow .NET Core patterns

### When Adding Dependencies

-   Pin exact versions using bracket notation: `[X.Y.Z]`
-   Prefer framework references over package references when possible
-   Check if dependency should be transitive or direct

## Build and Test Commands

```bash
# Build entire solution
dotnet build bitwarden-dotnet.sln

# Build specific project
dotnet build extensions/Bitwarden.Core/src/Bitwarden.Core.csproj

# Run all tests
dotnet test bitwarden-dotnet.sln

# Run tests for specific project
dotnet test extensions/Bitwarden.Core/tests/Bitwarden.Core.Tests.csproj

# Pack a package
dotnet pack extensions/Bitwarden.Core/src/Bitwarden.Core.csproj

# Restore dependencies
dotnet restore
```

## Common Tasks

### Add a New Extension Package

1. Create directory structure: `extensions/[PackageName]/src/`
2. Create `.csproj` with standard properties (see template above)
3. Add versioning properties
4. Create README.md
5. Add to solution: `dotnet sln add extensions/[PackageName]/src/[PackageName].csproj`
6. Add tests: `extensions/[PackageName]/tests/[PackageName].Tests.csproj`

### Update Package Version

-   For prerelease iteration: Modify `<PreReleaseVersionIteration>`
-   For prerelease label: Modify `<PreReleaseVersionLabel>` (alpha/beta/rc)
-   For major/minor/patch: Modify `<VersionPrefix>`

## Important Notes

### Security

-   See SECURITY.md for security policy
-   Authentication components handle sensitive JWT tokens
-   Secrets integration loads sensitive configuration

### Documentation Generation

-   XML documentation files generated for all packable projects
-   Embedded source for better debugging experience
-   PublishRepositoryUrl enabled for source link

## Troubleshooting

### Build Errors

-   Ensure .NET 8.0 SDK installed (check with `dotnet --version`)
-   Run `dotnet restore` to ensure all dependencies downloaded
-   Check that package versions are exact matches (brackets notation)

### Test Failures

-   Ensure Testcontainers is working if running integration tests
-   Check that test projects reference correct project versions
-   Verify xUnit test discovery is working

### Package Reference Issues

-   Use exact version pinning to avoid version conflicts
-   Check transitive dependencies don't conflict
-   Ensure project references use relative paths correctly

## Editor Configuration

-   `.editorconfig` file present for consistent formatting
-   IDE: JetBrains Rider compatible (`.idea` folder present)
-   Visual Studio 2022 compatible (solution format v17)
