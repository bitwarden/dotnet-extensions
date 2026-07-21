# Bitwarden.Server.Sdk.Features.CodeFixers

Roslyn code fixer companion to `Bitwarden.Server.Sdk.Features.Analyzers`.

## RemoveFeatureFlagCodeFixer

Fixes **BW0001** ("Feature flags should be removed once not used"). When invoked on a flag
constant it removes the constant and every reference to it across the entire solution.

### What it removes

| Usage pattern | Action |
|---------------|--------|
| `featureService.IsEnabled(Flag)` in an `if` condition | Inlines the `true` branch and drops the `else` branch |
| `featureService.IsEnabled(Flag)` in a binary expression | Simplifies the expression (`true && x` → `x`, `false \|\| x` → `x`, etc.) |
| `featureService.IsEnabled(Flag)` in a ternary | Inlines the `true` arm |
| `featureService.IsEnabled(Flag)` elsewhere | Replaces the call with `true` |
| `.RequireFeature(Flag)` at the end of a minimal-API chain | Removes the entire statement |
| `.RequireFeature(Flag)` in the middle of a chain | Removes just that method call |
| `[RequireFeature(Flag)]` attribute | Removes the attribute (and the attribute list if it was the only attribute) |
| NSubstitute mock: `.IsEnabled(Flag).Returns(false)` | Removes the mock setup line |
| NSubstitute mock: `.IsEnabled(Flag).Returns(false)` as the sole assertion | Removes the entire test method |
| Flag constant declaration | Removes the field from the `[FlagKeyCollection]` class |

After substituting `true` for removed `IsEnabled` calls, the fixer simplifies boolean expressions
and inlines literal `if` conditions. Unreachable statements that appear after an inlined
`return` or `throw` are also pruned.

Fix All is supported via `WellKnownFixAllProviders.BatchFixer`, so you can remove every flagged
constant in a project or solution in one action.
