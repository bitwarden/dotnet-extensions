# Diagnostics

## BW0001

**Title:** Feature flags should be removed once not used
**Severity:** Info
**Category:** Usage
**Package:** `Bitwarden.Server.Sdk.Features`
**Code fix available:** Yes

### Summary

Reported on every `const string` field inside a class marked with `[FlagKeyCollection]`. The diagnostic serves as a reminder to remove a feature flag and all of its usages once the flag has been fully rolled out (or rolled back).

### Details

When a flag is ready to be cleaned up, apply the accompanying code fix. It will:

- Remove the flag field from the `[FlagKeyCollection]` class.
- Replace `IsEnabled(<Flag>)` calls with `true`.
- Remove `RequireFeature(<Flag>)` calls from method chains.
- Remove `[RequireFeature(<Flag>)]` attributes.

### Example

```csharp
[FlagKeyCollection]
public static class FeatureFlags
{
    // BW0001 is reported here
    public const string MyFeature = "my-feature";
}
```

---

## BW0002

**Title:** Flag key value should be non-null or empty
**Severity:** Warning
**Category:** Usage
**Package:** `Bitwarden.Server.Sdk.Features`
**Code fix available:** No

### Summary

Reported when a `const string` field inside a `[FlagKeyCollection]` class has a `null`, empty, or whitespace-only value. Every flag key must have a non-empty string value so the flag can be matched at runtime.

### Example

```csharp
[FlagKeyCollection]
public static class FeatureFlags
{
    // BW0002 is reported here — value is empty
    public const string MyFeature = "";
}
```

**Fix:** Assign a non-empty string literal that matches the flag key registered in your feature flag service.

```csharp
[FlagKeyCollection]
public static class FeatureFlags
{
    public const string MyFeature = "my-feature";
}
```

---

## BW0003

**Title:** Should use `TryAdd` overloads
**Severity:** Warning
**Category:** Usage
**Package:** `Bitwarden.Server.Sdk`
**Code fix available:** Yes

### Summary

Reported when `AddSingleton`, `AddScoped`, or `AddTransient` (and their `AddKeyed*` variants) are called on an `IServiceCollection`. The `TryAdd*` overloads are preferred because they register the service only if no registration for that type already exists, preventing accidental duplicate registrations and making libraries safe to consume multiple times.

### Example

```csharp
// BW0003 reported on the following lines
services.AddSingleton<IMyService, MyService>();
services.AddScoped<IOtherService, OtherService>();
services.AddTransient<IThirdService, ThirdService>();
```

**Fix:** Use the `TryAdd*` equivalents:

```csharp
services.TryAddSingleton<IMyService, MyService>();
services.TryAddScoped<IOtherService, OtherService>();
services.TryAddTransient<IThirdService, ThirdService>();
```

The code fix handles both generic and non-generic overloads, as well as all `AddKeyed*` variants (`TryAddKeyedSingleton`, `TryAddKeyedScoped`, `TryAddKeyedTransient`).

---

## BW0004

**Title:** BitIncludeEnvironment is incompatible with dependent packages
**Severity:** Warning
**Category:** Configuration
**Package:** `Bitwarden.Server.Sdk`
**Code fix available:** No

### Summary

Reported when `BitIncludeEnvironment` is set to `false` while `BitIncludeFeatures` or `BitIncludeWebEssentials` is set to `true`. Both of those packages depend on `Bitwarden.Server.Sdk.Environment`, so `IBitwardenEnvironment` will still be available transitively even though `BitIncludeEnvironment` is `false`. Additionally, `AddBitwardenEnvironment()` will not be called by `UseBitwardenSdk()` in this configuration.

### Example

```xml
<!-- BW0004 is reported for this combination -->
<BitIncludeEnvironment>false</BitIncludeEnvironment>
<BitIncludeFeatures>true</BitIncludeFeatures>
```

**Fix:** Either set `BitIncludeEnvironment` to `true`, or disable the dependent packages:

```xml
<BitIncludeEnvironment>true</BitIncludeEnvironment>
<BitIncludeFeatures>true</BitIncludeFeatures>
```

To suppress the warning if the behavior is intentional:

```xml
<NoWarn>$(NoWarn);BW0004</NoWarn>
```

---

## BW0005

**Title:** Restricted dependency injected at a new site
**Severity:** Error
**Category:** RestrictedDependencies
**Package:** `Bitwarden.Server.Sdk.RestrictedDependencies`
**Code fix available:** No

### Summary

Reported when a constructor — classic or primary — takes a parameter of a type marked `[RestrictedDependency]` at a site that is not in that type's committed baseline. A restricted type is being dissolved into owned replacements, so its injection sites may shrink but not grow.

A parameter typed as a type parameter constrained to the restricted type counts the same way: `Consumer<T>(T userService) where T : IUserService` injects it as surely as naming it does.

### Example

```csharp
// BW0005 is reported on the parameter
public class BillingController(IUserService userService);
```

**Fix:** Use the owning team's command or query instead. If the new use is genuinely unavoidable, record it with `[RestrictedDependencyException]` rather than widening the baseline.

---

## BW0006

**Title:** Restricted member used beyond its budget
**Severity:** Error
**Category:** RestrictedDependencies
**Package:** `Bitwarden.Server.Sdk.RestrictedDependencies`
**Code fix available:** No

### Summary

Reported when a member of a restricted type is called or referenced more times inside one member than the baseline records, or at all when the member's rule is `AllowExistingUses = false`. A `nameof` is not a use. Uses inside a lambda or local function count against the method that contains them.

### Example

```csharp
public async Task Run(User user)
{
    await _userService.CanAccessPremium(user);
    await _userService.CanAccessPremium(user); // BW0006 when the baseline records one use
}
```

**Fix:** Call the `Replacement` named in the message, when there is one.

---

## BW0007

**Title:** Restricted dependency resolved from the service locator
**Severity:** Error
**Category:** RestrictedDependencies
**Package:** `Bitwarden.Server.Sdk.RestrictedDependencies`
**Code fix available:** No

### Summary

Reported when a restricted type is resolved through `GetService`, `GetRequiredService`, `GetServices`, their keyed variants, `RequestServices`, or `ActivatorUtilities` at a site not in the baseline. Resolving a restricted type from the container is the same dependency as injecting it, with the coupling hidden.

The method name alone is not enough — `CreateInstance` and `GetService` are ordinary names. The receiver has to be a container: `System.IServiceProvider` or a type implementing it (which covers `HttpContext.RequestServices`, `IServiceScope.ServiceProvider`, `IKeyedServiceProvider` and `ValidationContext`), or the static `ActivatorUtilities`. Instance, extension-method and static forms are all recognized.

Three things are therefore **not** locator resolutions, by design:

- A repository's own helper that happens to share a name, such as `MyOwnFactory.CreateInstance<T>()`. It resolves nothing from a container.
- Reflection through `System.Activator`. A `typeof` naming an implementation is still BW0008.
- Entity Framework's `DbContext.GetService<T>()`, whose receiver is `IInfrastructure<IServiceProvider>` rather than the provider itself.

> A hand-rolled static service locator is also not matched, because a static class cannot implement `IServiceProvider`. If a repository has one, the restricted types it hands out are invisible to this rule — audit for it directly rather than relying on BW0007.

### Example

```csharp
// BW0007 is reported here
var userService = provider.GetRequiredService<IUserService>();

// ...and here: an ASP.NET filter attribute cannot take constructor injection, so it
// reaches for the container instead. The dependency is real either way.
var userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
```

---

## BW0008

**Title:** Implementation of a restricted dependency referenced directly
**Severity:** Error
**Category:** RestrictedDependencies
**Package:** `Bitwarden.Server.Sdk.RestrictedDependencies`
**Code fix available:** No

### Summary

Reported when a type that implements a restricted type is referenced directly: static member access, `new`, inheritance, `typeof`, or a signature position. Reaching past the abstraction to the implementation defeats the restriction.

An interface that extends a restricted interface counts as an implementation, because it carries every restricted member — otherwise ordinary typed code would route straight around the ratchet. A member such a sub-interface re-declares with `new` maps to no restricted member, so BW0006 stays silent for it, but the site still carries this diagnostic. Declaring the sub-interface is not itself recorded, just as a class implementing a restricted interface is not recorded at its declaration.

Three things are exempt, because none of them is a use of the implementation:

- Type arguments and `typeof` arguments to `IServiceCollection` `Add*` / `TryAdd*` calls, which are registration.
- A `typeof` anywhere inside an attribute argument, such as `[ServiceFilter(typeof(UserService))]`, which is metadata.
- A reference from inside the referenced type itself, which is the type calling itself.

### Example

```csharp
// BW0008 is reported here
return UserService.IsLegacyUser(user);
```

---

## BW0009

**Title:** Restricted dependency escapes its consumer
**Severity:** Error
**Category:** RestrictedDependencies
**Package:** `Bitwarden.Server.Sdk.RestrictedDependencies`
**Code fix available:** No

### Summary

Reported when a restricted type leaves the class that holds it: through a non-private field or property, a non-constructor parameter, a return type, a generic argument such as `Lazy<T>` or `Func<T>`, a delegate signature, or a base-constructor argument. A private backing field is storage, not an escape.

A type parameter constrained to the restricted type counts in any of those positions, including buried in one — `Lazy<T> where T : IUserService`. A `this(...)` constructor chain is not an escape: nothing leaves the class, and the constructor it chains to already counts its parameter as injection.

An escape spreads the dependency to code that never asked for it, which is what makes the count hard to bring down.

### Example

```csharp
public abstract class BaseController
{
    // BW0009: every subclass now depends on the restricted type
    protected IUserService UserService { get; }
}
```

---

## BW0010

**Title:** Restricted dependency exception is incomplete
**Severity:** Error
**Category:** RestrictedDependencies
**Package:** `Bitwarden.Server.Sdk.RestrictedDependencies`
**Code fix available:** No

### Summary

Reported when `[RestrictedDependencyException]` is missing `Owner`, `Reason`, or an `Expires` in `yyyy-MM-dd` form, or when its first argument is not a `typeof()` of a named type. An invalid exception excepts nothing, so a half-written attribute fails closed and the underlying diagnostic is still reported.

### Example

```csharp
// BW0010: Reason and Expires are missing
[RestrictedDependencyException(typeof(IUserService), Owner = "team-billing")]
public class BillingController;
```

**Fix:**

```csharp
[RestrictedDependencyException(typeof(IUserService),
    Owner = "team-billing", Reason = "PM-12345", Expires = "2026-12-01")]
public class BillingController;
```

---

## BW0011

**Title:** Restricted dependency exception has expired
**Severity:** Warning
**Category:** RestrictedDependencies
**Package:** `Bitwarden.Server.Sdk.RestrictedDependencies`
**Code fix available:** No

### Summary

Reported when a valid `[RestrictedDependencyException]` has passed its `Expires` date. A warning rather than an error so that a lapsed expiry does not block unrelated work.

This is the one diagnostic in the family that may be configured below error; BW0016 exempts it.

**Fix:** Remove the use, or renew the date with the owner's agreement.

> This is advisory only. Excepted sites are deliberately kept out of the baselines, so a shrink-only baseline check never sees them either. Nothing chases an expiry automatically.

---

## BW0012

**Title:** Restricted dependency diagnostic suppressed without an exception
**Severity:** Error
**Category:** RestrictedDependencies
**Package:** `Bitwarden.Server.Sdk.RestrictedDependencies`
**Code fix available:** No

### Summary

Reported when `#pragma warning disable` or `[SuppressMessage]` names one of BW0005-BW0017. `[SuppressMessage]` silences the diagnostic it names; a pragma does not, because the error ids are not configurable (see BW0016), but it is reported all the same. A bare `#pragma warning disable` with no ids silences everything configurable, including BW0011, and is reported under that description.

The diagnostic is deliberately reported **without a source location**, because one placed inside the suppressed region would be silenced by the very directive it is reporting.

> A global `[SuppressMessage]` (assembly or module scope) naming BW0012 silences every BW0012, including the one about itself, because global suppressions reach location-less diagnostics too. A consuming repository should scan its production source for global suppressions of these ids.

> Pragmas inside a compiler-generated syntax tree — source generator output, or any file an `.editorconfig` marks `generated_code = true` — are not scanned, so a `#pragma warning disable` there is not reported as BW0012. This costs nothing for the error ids, which stay `NotConfigurable` regardless; it only matters for BW0011, the one id a pragma could otherwise be shown to have silenced. No source generator emits `[RestrictedDependencyException]` today, so the gap is theoretical until a hand-written file is marked `generated_code = true`.

### Example

```csharp
#pragma warning disable BW0005 // BW0012 is reported, and BW0005 still is
public BillingController(IUserService userService) { }
#pragma warning restore BW0005
```

**Fix:** Use `[RestrictedDependencyException]` with an owner, reason and expiry. A suppression hides the debt; an exception records who owns it and when it should be gone.

---

## BW0013

**Title:** Baseline entry no longer matches the code
**Severity:** Error
**Category:** RestrictedDependencies
**Package:** `Bitwarden.Server.Sdk.RestrictedDependencies`
**Code fix available:** No

### Summary

Reported when a baseline row for this project matches nothing, or fewer uses than its `count`. The budget can then only be spent down deliberately rather than drifting: a removed use has to be committed as a smaller baseline.

Only gated rules are checked. A tracked-only entry — `AllowNewUses = true` — is a snapshot for reporting, so its disappearance is not reported. Such a row is written carrying `"tracked": true`, which is what also keeps it out of the shrink-only comparison in `BudgetRatchet.FindGrowth`: a total the rule allows to rise must not fail the baseline check.

Renaming or moving a method that holds baselined uses changes its key, so the build reports BW0013 for the old key and BW0006 for the new one until the baseline is regenerated. A shrink-only check reads that as a net-zero move and accepts it.

**Fix:** Regenerate the baselines. The message quotes the command configured in `RestrictedDependencyUpdateCommand`.

---

## BW0014

**Title:** Sealed type gained a member
**Severity:** Error
**Category:** RestrictedDependencies
**Package:** `Bitwarden.Server.Sdk.RestrictedDependencies`
**Code fix available:** No

### Summary

Reported when a type marked `SealMembers`, `SealNestedTypes` or `SealProperties` declares something the seal covers that is not in its baseline's `declaredMembers`. Freezing the shape is what stops a type that is supposed to be dissolving from growing instead.

The seal covers methods, events, properties and nested types. Fields, constructors and user-defined operators are not covered by it.

### Example

```csharp
[RestrictedDependency(SealMembers = true)]
public interface IUserService
{
    Task<bool> CanAccessPremium(User user);
    Task DoSomethingNew(User user); // BW0014
}
```

**Fix:** Put the new behavior in the owning team's own type.

---

## BW0015

**Title:** Restricted dependency attribute or baseline is invalid
**Severity:** Error
**Category:** RestrictedDependencies
**Package:** `Bitwarden.Server.Sdk.RestrictedDependencies`
**Code fix available:** No

### Summary

Reported for anything that makes the attribute or baseline unreadable rather than violated:

- `AllowExistingUses = false` combined with `AllowNewUses = true`, which has no meaning.
- A type-level setting such as `SealMembers` or `AllowedPaths` applied to a member.
- An `AllowedPaths` glob that is not a repo-relative forward-slash path.
- A `[RestrictedDependency]` on a member of a type that does not carry the attribute itself, where it governs nothing: member rules are only read off a restricted type.
- A type carrying `[RestrictedDependency]` with no baseline supplied to the compilation.
- Analysis enabled for a project with no baseline supplied at all, which gates nothing. Set `RestrictedDependencyBaselinesPath`, or turn the analysis off for that project.
- Seed types supplied without BW0017 enabled through `CompilationOptions.WithSpecificDiagnosticOptions`, which no baseline tool would do. The build stays enforcing.
- A baseline that is unreadable, malformed, duplicated, or orphaned — its type no longer carries the attribute.

### Example

```csharp
// BW0015: AllowedPaths must use forward slashes, relative to the repository root
[RestrictedDependency(AllowedPaths = new[] { @"src\Core\**" })]
public interface IUserService;
```

---

## BW0016

**Title:** Restricted dependency diagnostic configured below error
**Severity:** Error
**Category:** RestrictedDependencies
**Package:** `Bitwarden.Server.Sdk.RestrictedDependencies`
**Code fix available:** No

### Summary

Reported when BW0005-BW0010 or BW0012-BW0016 is configured below error by `.editorconfig`, a global analyzer config, `NoWarn`, or `WarningsNotAsErrors`. BW0011 is exempt because it is a warning by design, and BW0017 because it is configured on and off by baseline tooling.

The configuration has no effect: these ids are not configurable, so the build still fails on the diagnostic it tried to lower, and BW0016 points at the dead line. Bulk keys (`dotnet_analyzer_diagnostic.*`) are equally ineffective but are not reported.

Reported without a location, because the configuration is not in the compilation.

### Example

```ini
# BW0016 is reported
dotnet_diagnostic.BW0005.severity = warning
```

**Fix:** Remove the configuration and use `[RestrictedDependencyException]` on the site instead; an exception exempts one site, with an owner and a date.

> A consuming repository should still check its own build configuration directly: a project can switch the analyzer off entirely, which no diagnostic can report. `DiagnosticConstants.MustRemainErrors` lists the ids such a check covers.

---

## BW0017

**Title:** Restricted dependency observation
**Severity:** Hidden, and disabled by default
**Category:** RestrictedDependencies
**Package:** `Bitwarden.Server.Sdk.RestrictedDependencies`
**Code fix available:** No

### Summary

Not a rule. This is how a repository's baseline tool reads what the analyzer saw, so it can regenerate baselines by hosting the same analyzer the compiler runs rather than a second scanner that could disagree with it.

It is `isEnabledByDefault: false`, so a normal build emits nothing and pays nothing. Compliant code produces no diagnostic by definition, which is why the rows that build a baseline need a channel of their own.

### Details

A tool enables it and names the restricted types, because in observe mode there is no baseline to seed from:

```csharp
var options = compilation.Options.WithSpecificDiagnosticOptions(
    ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(ObservationConstants.DiagnosticId, ReportDiagnostic.Info));
// and AnalyzerConfigConstants.SeedTypes = "Some.Restricted.IType;Another.IType"
```

Both halves are required to enter observe mode, and the analyzer enforces nothing once in it, because it cannot treat the baseline it is rebuilding as the authority. A seed list with the channel left off is not a baseline tool: that build stays enforcing and reports BW0015. Only compiler options can reach `SpecificDiagnosticOptions`, so the seed key alone — which an `.editorconfig` can set — cannot stand a project down.

Each diagnostic carries one row in `Diagnostic.Properties`, discriminated by `row`: a `usage`, a `declared-member`, an `exception`, or a `restricted-type`. The property names are constants on `ObservationConstants` in the `Bitwarden.Server.Sdk.RestrictedDependencies` namespace, so a tool does not spell them twice.
