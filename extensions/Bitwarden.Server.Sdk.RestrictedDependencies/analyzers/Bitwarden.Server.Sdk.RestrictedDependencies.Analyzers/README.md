# Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers

Roslyn analyzer and incremental source generator for the Bitwarden Restricted Dependencies feature.

## Contents

- **`RestrictedDependencyAnalyzer`** - Diagnostic analyzer that enforces restricted use of dependencies deemed ready for deprecation.
- **`AttributeGenerator`** - Incremental source generator emitting `[RestrictedDependency]` and `[RestrictedDependencyException]` into every analyzed compilation.

## Architecture

The generator and the analyzer are two independent Roslyn passes; the generator's output becomes part of what the analyzer sees.

```mermaid
flowchart LR
    G1["AttributeGenerator.Initialize"] --> G2{"RestrictedDependencyAnalysis enabled?"}
    G2 -- "yes" --> G3["emit AttributeConstants.Text<br/>into the compilation"]
    G2 -- "no" --> G4["emit nothing"]
```

`RestrictedDependencyAnalyzer` builds one `CompilationCoordinator` per compilation. It resolves the committed baselines and restricted types once, classifies every restricted-type use it sees during the compilation, and either reconciles against the baseline or reports observations at the end, depending on whether the compiler or a baseline tool is hosting it:

```mermaid
flowchart TD
    Init["RestrictedDependencyAnalyzer.Initialize"] --> Gate{"RestrictedDependencyAnalysis enabled?<br/>RestrictedDependencyConfig.IsEnabled"}
    Gate -- "no" --> Skip["skip this compilation entirely"]
    Gate -- "yes" --> Ctor["CompilationCoordinator constructed"]

    subgraph Construct["Once per compilation"]
        Ctor --> Sev["DiagnosticSeverityScanner.Run<br/>NoWarn / .editorconfig lowering a family id → BW0016"]
        Ctor --> Base["BudgetIndex.Load<br/>committed baseline JSON → BW0015 on unreadable/duplicate files"]
        Ctor --> Pol["RestrictedTypeIndex.Resolve<br/>read [RestrictedDependency] off seed types or baseline types<br/>→ BW0015 on bad attributes, flags orphaned baselines"]
        Pol --> Mode{"SeedTypes supplied<br/>and BW0017 enabled?"}
        Mode -- "both: a baseline tool is hosting" --> Observe["Observe mode — nothing enforced"]
        Mode -- "neither: the compiler is hosting" --> Enforce["Enforce mode"]
        Mode -- "seed only: not a tool" --> Enforce
        Enforce --> EnforceCheck["→ BW0015 on an unhonoured seed,<br/>or on analysis enabled with no baseline"]
        Ctor --> Collab["build collaborators:<br/>DependencyExceptionTracker, DeclaredMemberSetChecker,<br/>BudgetReconciler, RestrictedDependencyUseClassifier"]
        Ctor --> Suppress0["scan assembly + module suppressions<br/>→ BW0012 candidates"]
    end

    Collab --> CB["per-symbol / per-operation callbacks"]

    subgraph Callbacks["named types, methods, properties, fields, events, operations, syntax trees"]
        CB --> Insp["Inspect: suppression scan + exception validation<br/>→ BW0010 / BW0011 / BW0012"]
        CB --> Attributed{"[RestrictedDependency] type?"}
        Attributed -- "yes" --> MemberSet["DeclaredMemberSetChecker.Check<br/>→ BW0015 if no baseline;<br/>if sealed, compare declared members → BW0014"]
        CB --> HasRestricted{"any restricted type in scope?"}
        HasRestricted -- "yes" --> Classify["RestrictedDependencyUseClassifier<br/>classify: Injection / Member / Locator / Concrete / Escape"]
        Classify --> Allowed{"AllowedPaths match?"}
        Allowed -- "yes" --> Drop["exempt — dropped, not even counted"]
        Allowed -- "no" --> Excepted{"valid RestrictedDependencyException covers it?"}
        Excepted -- "yes" --> AddExcepted["Ledger.AddExcepted<br/>never counted toward the baseline"]
        Excepted -- "no" --> Forbidden{"forbidden, and enforcing?"}
        Forbidden -- "yes" --> ReportNow["report BW0005-0009 immediately"]
        ReportNow --> AddCounted
        Forbidden -- "no" --> AddCounted["Ledger.Add<br/>counted; gated rules reconciled against budget at compilation end"]
    end

    AddCounted --> End["OnCompilationEnd"]
    AddExcepted --> End
    MemberSet --> End

    End --> Startup["report accumulated startup diagnostics"]
    Startup --> ModeEnd{"observe or enforce?"}
    ModeEnd -- "Enforce" --> Recon["BudgetReconciler"]
    Recon --> Excess["ExcessUses → BW0005-0009 on surplus locations"]
    Recon --> Stale["StaleEntries → BW0013"]
    ModeEnd -- "Observe" --> Obs["Ledger / RestrictedTypes / Exceptions .Observations()<br/>DeclaredMemberSetChecker.Observed"]
    Obs --> BW17["BW0017 rows: usage, declared-member, exception, restricted-type"]
```

`RestrictedDependencyUseClassifier` picks one of five usage kinds for a given use, each mapping to one BW0005-0009 diagnostic:

```mermaid
flowchart TD
    Use["a type, member, or operation touches a restricted type"] --> Where{"where does it appear?"}
    Where -- "constructor parameter" --> Inj["Injection (BW0005)"]
    Where -- "member call / reference" --> Mem["Member (BW0006)"]
    Where -- "GetService / GetRequiredService / locator method" --> Loc["Locator (BW0007)"]
    Where -- "implementing type referenced directly:<br/>new, static call, typeof, base type" --> Con["Concrete (BW0008)"]
    Where -- "buried in a signature, field, generic argument, or delegate" --> Esc["Escape (BW0009)"]
```

## Diagnostics

BW0005-BW0017. See [docs/diagnostics.md](https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md).

## Running tests

```bash
dotnet run --project tests/Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests
dotnet run --project tests/Bitwarden.Server.Sdk.RestrictedDependencies.Tests
```
