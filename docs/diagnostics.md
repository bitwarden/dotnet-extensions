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
