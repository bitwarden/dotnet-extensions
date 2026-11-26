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
-   A new dependency needs a Dependency Review and team agreement before the pull request. See [Dependencies](#dependencies) under Lessons from Code Review.

## Lessons from Code Review

Reviewers raised each of these on a pull request. Check every change against them before opening or updating a pull request, so no reviewer has to raise one twice.

### Code

-   Brace every `if`, `else`, `for`, `foreach`, `while`, `do`, `using`, `lock` and `fixed` body, even a single `return` or `continue`. `.editorconfig` enforces this, and CI's `dotnet format --verify-no-changes` fails without it.
-   Don't add a parameter that every caller sets to the same value as another parameter. Use the existing one, and add a new one only when a caller passes something different.
-   Put each top-level type in its own file, named for it. Nested private types can stay.
-   Don't give a base type the same name as the type that implements it. If every member of an abstract class is abstract, make it an interface.
-   Leave out comments that only restate the name of the thing they describe.
-   A parser that claims to be strict refuses everything the underlying BCL parser also accepts: whitespace, sign and hex prefixes, braces, leading zeros, legacy forms.
-   After moving or deleting code, remove any `PackageReference` that is no longer used, in src and tests, and commit the updated lock files.

### Tests

-   For theory cases longer than short scalars, use `ClassData` or `TheoryData` rows with a readable `TestDisplayName`, not long `[InlineData]` strings of code.
-   Give each branch and each lookup-table row a case where only that branch decides the outcome, and assert the full message text, not only an id. Check that each test actually reaches the state its name claims.
-   Pin every test container image by digest, with its version in a comment: `"rabbitmq@sha256:…"; // 4.4.1`. When a package has a shared definition of an image, use it.

### Dependencies

-   A new dependency of any kind, including a container image or a broker used only in CI, needs a Dependency Review (a VULN ticket with AppSec sign-off) and team agreement before the pull request. Keep it in its own pull request, with nothing else depending on it.

### Documentation and pull requests

-   Don't add file-by-file tables to a README. Describe each type in a short XML `<summary>` instead.
-   Where a document describes a flow or a topology, add a Mermaid diagram. Name participants by their role in a service-oriented architecture (sending service, message broker, receiving service), not only by type name.
-   Write messages, diagnostics and summaries in short, plain sentences that name the exact operation and object and say how to fix the problem.
-   Every pull request has a Jira work item saying, in a human voice, why the change is needed and which problems it prevents.
-   Keep the pull request description short at first glance, with detail in `<details>` blocks. Keep any AI-assisted review section to a line or two: it distracts the reviewer agents that read the description later.

### Adding a lesson

When a human reviewer's comment applies beyond the pull request it was made on, record it in its own pull request. If a tool can check it, enforce it in `.editorconfig`, an analyzer or CI, and list it here too when knowing it up front saves a failing CI run. Otherwise, add it to this section.

## Important Notes

### Security

-   See SECURITY.md for security policy
-   Authentication components handle sensitive JWT tokens
-   Secrets integration loads sensitive configuration

### No hand-built crypto

-   Make and check tokens, signatures, key formats and encrypted formats (JWT, JWS, JWE, JWK, COSE, EncString) with the library this repository already uses: `Microsoft.IdentityModel` for JWTs and JWKs, `Bitwarden.Core` for EncStrings. Never compose one by hand from `System.Security.Cryptography` primitives, even when the result would be correct and the library would add a dependency, unless the owning team agrees (below).
-   Hashing, keyed digests (HMAC) and key derivation are crypto too. Use the Key Management packages that provide them: `Bitwarden.Server.Sdk.CryptoPrimitives` for keyed digests such as cache keys, and `Bitwarden.Server.Sdk.PasswordHashing` for password hashes. Anything they don't cover needs the owning team's agreement, like any other hand-built crypto. `Bitwarden.Core`'s own implementation of the EncString format is not hand-built crypto. Tests may build malformed tokens by hand.
-   Key Management (`@bitwarden/team-key-management-dev`) owns cryptography in this repository; team-auth owns authentication and tokens.
-   Hand-building one anyway needs the owning team's agreement first. Record it in a comment on the code that says what it does, why no library serves it, and which team agreed where (an ADR, a decision or a pull request).
-   A pull request that adds or changes hand-built crypto is blocked until a human has reviewed that code and commented on the pull request about it, even when the code carries an agreement comment. A Claude review that finds it reports it as a critical finding, and does not count as that human review.
-   For work on authentication, tokens or crypto, agree the design and the library with the owning team before writing code.
-   When code deliberately avoids a library the repository already has, say why in one line in the pull request description.

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
