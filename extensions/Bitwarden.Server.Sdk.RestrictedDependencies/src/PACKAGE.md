# Bitwarden.Server.Sdk.RestrictedDependencies

## About

Some types outgrow their purpose: a service that every team injects, a settings class that every team adds a property to. This package lets a repository freeze one, record every existing use in a committed baseline, and make the build reject new ones — so the type can only shrink while it is dissolved into owned replacements.

It ships a Roslyn analyzer, a source generator that emits the attributes, and the baseline document format so a repository can regenerate and diff its own baselines.

## How to use

Turn the analysis on for the projects you want gated, and point it at your committed baselines:

```xml
<PropertyGroup>
  <RestrictedDependencyAnalysis>true</RestrictedDependencyAnalysis>
  <RepoRoot>$(MSBuildThisFileDirectory)</RepoRoot>
  <RestrictedDependencyBaselinesPath>$(RepoRoot)baselines</RestrictedDependencyBaselinesPath>
  <RestrictedDependencyUpdateCommand>dotnet run --project tools/Baselines -- update</RestrictedDependencyUpdateCommand>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Bitwarden.Server.Sdk.RestrictedDependencies" Version="..." IncludeAssets="analyzers;build" />
</ItemGroup>
```

| Property                            | Meaning                                                                                |
| ----------------------------------- | -------------------------------------------------------------------------------------- |
| `RestrictedDependencyAnalysis`      | `true` to analyze this project. Anything else is skipped before a symbol is looked at. |
| `RepoRoot`                          | Repository root, so paths in messages and `AllowedPaths` globs are repo-relative.      |
| `RestrictedDependencyBaselinesPath` | Directory of committed `<Type>.json` baselines.                                        |
| `RestrictedDependencyUpdateCommand` | Your regenerate command, quoted back in BW0013 and BW0015.                             |

Then mark a type. The attributes are generated into every analyzed compilation, so there is nothing to reference:

```csharp
[RestrictedDependency(AllowExistingUses = true, AllowNewUses = false,
    Tracking = "PM-43148", Owner = "@my-team",
    SealMembers = true, AllowedPaths = ["src/Core/Services/Implementations/UserService.cs"])]
public interface IUserService
{
    [RestrictedDependency(Replacement = "IHasPremiumAccessQuery.HasPremiumAccessAsync")]
    Task<bool> CanAccessPremium(User user);
}
```

The `(AllowExistingUses, AllowNewUses)` pair decides how a use is treated:

| Rule             | Meaning                                                               |
| ---------------- | --------------------------------------------------------------------- |
| `(true, true)`   | Tracked only. Counted in the baseline as a `tracked` row, never a diagnostic, and exempt from the shrink-only check. |
| `(true, false)`  | Gated. Baselined uses pass; anything beyond the baseline is an error. |
| `(false, false)` | Forbidden. Every use is an error, baselined or not.                   |
| `(false, true)`  | Invalid; rejected by BW0015.                                          |

`AllowedPaths` is a full exemption: a matching file is neither gated, baselined nor observed for this type, so name the implementation file rather than its folder.

### Exceptions

When a new use is unavoidable, put an owned, expiring exception on the site rather than widening the baseline. All three properties are required, and an invalid exception excepts nothing:

```csharp
[RestrictedDependencyException(typeof(IUserService),
    Owner = "team-billing", Reason = "PM-12345", Expires = "2026-12-01")]
public class ProviderBillingController(IUserService userService);
```

> **`Expires` is advisory.** BW0011 is a warning, and excepted sites are deliberately kept out of the baselines, so a shrink-only check never sees them either. Nothing chases an expiry for you. Take an exception because the owner and reason are worth recording, not because the date will be enforced.

## Baselines

A baseline is one JSON document per restricted type, listing every use keyed by the documentation-comment id of the containing member — so it survives line moves and is overload-safe. It carries no timestamp or commit hash: two runs on the same tree serialize byte-identically. Its `type` field is the name `Compilation.GetTypeByMetadataName` accepts: namespace, dot, nested names joined by `+`, arity suffix included.

A row whose rule is tracked-only carries `"tracked": true`, which `BudgetRatchet.FindGrowth` reads to exempt it: that total is expected to rise, so comparing it would fail the check for exactly the code the rule permits. The flag is written only when true, so a document with no tracked-only rows is byte-identical to one written without it. Your tool takes the value off the `tracked` property of each BW0017 usage row.

`BudgetModel`, `BudgetEntry`, `BudgetRatchet.FindGrowth`, `DependencyUsageType` and `DependencyUsageTypeExtensions` are the supported way to read, write and diff those documents from your own tooling. Your tool project references the package without the `IncludeAssets` trim, so the library is compiled in. The package is a development dependency, so it stays with the project that references it and does not flow to anything that depends on that project.

Your tool hosts the same analyzer the compiler does rather than reimplementing the scan. Enable BW0017 through `CompilationOptions.WithSpecificDiagnosticOptions`, supply the analyzer-config keys on `AnalyzerConfigConstants` (`Analysis`, `RepositoryRoot`, `SeedTypes`, joined by `SeedTypeSeparator`), and read the rows back off `Diagnostic.Properties` using `ObservationConstants`. `AttributeConstants` gives you the attribute name to discover restricted types by, and the attribute `Text` and `HintName` to compile in when the generator has not run. BW0017 is disabled by default, so it emits nothing in a real build. `DiagnosticConstants` carries the family's ids for your own configuration checks.

## Diagnostics

| ID                                                                                            | Severity | Fires when                                                                            |
| --------------------------------------------------------------------------------------------- | -------- | ------------------------------------------------------------------------------------- |
| [BW0005](https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#bw0005) | Error    | A constructor takes a restricted type at a site not in the baseline.                  |
| [BW0006](https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#bw0006) | Error    | A restricted member is used beyond its baselined count, or at all when forbidden.     |
| [BW0007](https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#bw0007) | Error    | A restricted type is resolved from the service locator at a site not in the baseline. |
| [BW0008](https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#bw0008) | Error    | An implementing type is referenced directly.                                          |
| [BW0009](https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#bw0009) | Error    | A restricted type escapes its consumer.                                               |
| [BW0010](https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#bw0010) | Error    | An exception is missing `Owner`, `Reason` or a `yyyy-MM-dd` `Expires`.                |
| [BW0011](https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#bw0011) | Warning  | An exception has expired.                                                             |
| [BW0012](https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#bw0012) | Error    | A pragma or `[SuppressMessage]` names one of these ids.                               |
| [BW0013](https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#bw0013) | Error    | A baseline entry no longer matches the code.                                          |
| [BW0014](https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#bw0014) | Error    | A sealed type gained a member, nested type or property.                               |
| [BW0015](https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#bw0015) | Error    | An attribute or a baseline is invalid, missing or orphaned.                           |
| [BW0016](https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#bw0016) | Error    | One of these ids is configured below error; the setting has no effect.                |
| [BW0017](https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#bw0017) | Hidden   | Never in a real build. The observation channel for baseline tooling.                  |
