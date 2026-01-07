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

-   **Bitwarden.Extensions.Configuration** - Configuration integration
-   **Bitwarden.Server.Sdk** - Main SDK for building Bitwarden services

## Testing Standards

### Test Framework

-   **xUnit.v3**
-   **Microsoft.Testing.Extensions.CodeCoverage** for code coverage

### Additional Test Tools

-   `NSubstitute` for mocking
-   `Microsoft.AspNetCore.TestHost` for integration tests
-   `Testcontainers` for container-based tests
-   `MSBuild.ProjectCreation` for MSBuild testing

### Test Project Structure

-   Test projects are in `tests/` subfolder of each extension
-   Test projects use exact version pinning: `Version="[X.Y.Z]"`
-   `<Using Include="Xunit" />` for global xUnit usings

## Code Style and Conventions

### C# Language Features

-   You must follow all code-formatting and naming conventions defined in [`.editorconfig`](/.editorconfig)
-   **Implicit usings enabled** - Common namespaces automatically included
-   **Nullable reference types enabled** - All projects use `<Nullable>enable</Nullable>`
-   File-scoped namespaces preferred (modern C# style)

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
-   `.github/workflows/prerelease.yml` - Create prereleases (alpha, beta, rc) to NuGet
-   `.github/workflows/release.yml` - Full release to NuGet

### Version Bumping

-   **Automatic**: Minor and patch versions via workflows
-   **Manual**: Major versions and prerelease label changes (alpha → beta → rc)
-   Prerelease format: `X.Y.Z-[label].[iteration]` (e.g., `1.1.0-beta.2`)

### New Package Setup

1. Create folder: `extensions/[PackageId]/src/`
2. Add versioning properties to `.csproj`
3. Add package ID to `start-release.yml` workflow inputs
4. Create `PACKAGE.md` file in source root for documentation for consumers of the package
5. Create `README.md` file in source root for documentation for developers of the package

## Development Guidelines

### When Adding New Features

1. **Add XML documentation**: All public APIs require XML docs (`<GenerateDocumentationFile>true</GenerateDocumentationFile>`)
2. **Write tests**: Unit tests required, integration tests for complex features
3. **Update PACKAGE**: Each package has its own PACKAGE.md

### When Working with Configuration

-   Secrets integration in `Bitwarden.Extensions.Configuration`
-   Parallel loading supported via `ParallelSecretLoader`
-   Custom configuration sources follow .NET Core patterns

### When Adding Dependencies

-   Pin exact versions using bracket notation: `[X.Y.Z]` in test projects
-   Do not pin exact versions in packable projects

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

-   Ensure .NET 10.0 SDK installed (check with `dotnet --version`)
-   Run `dotnet restore` to ensure all dependencies downloaded

### Test Failures

-   Ensure Testcontainers is working if running integration tests
-   Check that test projects reference correct project versions
-   Verify xUnit test discovery is working

### Package Reference Issues

-   Check transitive dependencies don't conflict
-   Ensure project references use relative paths correctly

## Editor Configuration

-   `.editorconfig` file present for consistent formatting
-   IDE: JetBrains Rider compatible (`.idea` folder present)
-   Visual Studio 2022 compatible (solution format v17)
