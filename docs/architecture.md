# FaultWitness architecture

Start at [src/README.md](../src/README.md) for a short project directory guide. This page describes the current repository structure and the direction of its direct project references.

## Product flow

```mermaid
flowchart LR
  Windows[Windows diagnostic sources] --> Collect[Collect]
  Collect --> Normalize[Normalize to EventBatch]
  Files[Supported saved files] --> Import[Import and normalize]
  Import --> Normalize
  Normalize --> Analyze[Core IncidentAnalyzer + Rules]
  Analyze --> Enrich[Correlate and enrich]
  Enrich --> Present[App presentation]
  Present --> Persist[Storage: local history and Action Journal]
  Present --> Export[Export: user-requested reports]
```

For a local recent or around-time analysis, `DesktopServices` reads Windows sources, creates normalized records, runs `IncidentAnalyzer` with the rule catalog, and enriches the result with retained occurrences and Windows change history. Supported imports enter through `WindowsImportService`; their records and partial source coverage go through the same analyzer and result presentation. The App stores summaries locally and exports only when requested. Persistence and export are separate consumers of the presented result.

## Production project references

Arrows point from a project to a direct `ProjectReference` dependency in its current `.csproj`. The test projects are omitted. The App-to-Helper edge builds and stages the helper payload with `ReferenceOutputAssembly=false`; it is not a runtime assembly reference. The graph is acyclic.

```mermaid
flowchart TD
  App[FaultWitness.App] --> Design[FaultWitness.Design]
  App --> Core[FaultWitness.Core]
  App --> Platform[FaultWitness.Platform]
  App --> Windows[FaultWitness.Platform.Windows]
  App --> Rules[FaultWitness.Rules]
  App --> Storage[FaultWitness.Storage]
  App --> Export[FaultWitness.Export]
  App --> Localization[FaultWitness.Localization]
  App -. build and payload only .-> Helper[FaultWitness.ElevatedHelper]

  Cli[FaultWitness.Cli] --> Core
  Cli --> Platform
  Cli --> Windows
  Cli --> Rules
  Cli --> Export
  Helper --> Core
  Helper --> Windows
  Windows --> Core
  Windows --> Platform
  Platform --> Core
  Rules --> Core
  Storage --> Core
  Export --> Core
  Export --> Localization
```

## Project responsibilities and boundaries

| Project | Responsibility and reason for the boundary |
|---|---|
| `FaultWitness.App` | Avalonia desktop shell, pages, view model, presentation adapters, settings, and Windows desktop composition. The GUI normally runs as the signed-in user. Its current visual trees and styles are constructed in C#; there are no current AXAML views. A separate beta.2 pass is planned to introduce AXAML/UserControls and page/component ownership incrementally. |
| `FaultWitness.Core` | Platform-neutral event, incident, evidence, coverage, inventory, and capture-policy semantics; analysis, correlation, and neutral capture contracts. Keeping Windows APIs and UI out of Core lets diagnostic meaning be reviewed and tested without an operating-system collector. |
| `FaultWitness.Platform` | Neutral collection contracts and change-history enrichment orchestration. It keeps acquisition seams and enrichment outside Core's diagnostic semantics. |
| `FaultWitness.Platform.Windows` | Windows Event Log, WER, Reliability, dump-artifact, import, readiness, inventory, change-history, registry, and helper-client behavior. Windows APIs and OS-specific normalization stay at this boundary. |
| `FaultWitness.Rules` | Provider-aware diagnostic policy, rule definitions, and diagnostic facts. Keeping rules separate makes supported evidence thresholds and false-positive controls visible without moving policy into the neutral engine or UI. |
| `FaultWitness.Storage` | SQLite-backed analysis history and capture Action Journal. SQLite schema and persistence policy stay outside Core. |
| `FaultWitness.Export` | Shared report and system-summary export used by App and CLI, with localized presentation. It does not depend on Avalonia or Storage. |
| `FaultWitness.Localization` | Eight shared resource sets and `LocalizationService`, referenced by App and Export so translated product wording has one owner. |
| `FaultWitness.Design` | Reads and validates the canonical design tokens embedded from `docs/design/faultwitness.tokens.json`; the App consumes its resolved design resources. |
| `FaultWitness.Cli` | Separate Windows command-line entry point for scan, around-time, import, export, and version operations. It shares Core, platform collection, Rules, and Export without hosting the GUI. |
| `FaultWitness.ElevatedHelper` | Separate one-shot administrator process with a bounded request protocol. It is staged beside the normal App and performs only the permitted capture-configuration mutation. Process separation keeps routine diagnostics and the GUI unelevated. |

These are repository boundaries, not separately released SDKs. The current desktop composition is Windows-specific: Core is neutral and Windows collection is isolated, but App targets and composes Windows services. Linux or macOS support would need real collectors, rules, and platform composition for those systems; a build switch alone would not provide it.

## Privileged capture flow

```mermaid
sequenceDiagram
  actor User
  participant App as Normal FaultWitness app
  participant Journal as Local Action Journal
  participant UAC as Windows UAC
  participant Helper as ElevatedHelper
  participant Registry as Windows LocalDumps settings
  User->>App: Explicit Preview request
  App->>Registry: Read current supported state
  Registry-->>App: Preview state and support status
  User->>App: Confirm the specific Configure or Restore action
  App->>Journal: Persist bounded action intent
  App->>UAC: Launch exact helper request
  UAC->>User: Administrator approval prompt
  User->>UAC: Approve or cancel
  UAC->>Helper: Start separate elevated process
  Helper->>Registry: Apply only the permitted mutation
  Helper->>Registry: Reread and verify resulting state
  Helper-->>App: Bounded result
  App->>Journal: Record verified result
  User->>App: Explicit guarded Restore
  App->>Registry: Reread, check drift and newer actions
  App->>UAC: Request restore only when guards pass
```

The journal stores intent before dispatch and the verified result afterward so an interrupted operation remains distinguishable from success. Restore checks the current state and newer unreverted actions before requesting the previous supported state. The App itself does not normally run elevated, and the helper protocol does not accept arbitrary registry paths or commands.

## Where do I change X?

| Change | Current files |
|---|---|
| Home/Analyze copy | `src/FaultWitness.App/MainWindow.Analysis.cs`; `src/FaultWitness.Localization/Strings*.resx` |
| Incident Detail UI | `src/FaultWitness.App/MainWindow.Detail.cs`; `src/FaultWitness.App/EvidencePresentation.cs` |
| Diagnostic rule | `src/FaultWitness.Rules/RuleCatalog.cs`; `src/FaultWitness.Rules/ProviderAwareRule.cs`; `src/FaultWitness.Rules/DiagnosticFacts.cs` |
| Negative evidence / coverage | `src/FaultWitness.Core/CoveragePolicy.cs`; `src/FaultWitness.Core/Analysis.cs`; presentation in `src/FaultWitness.App/EvidencePresentation.cs` |
| Windows normalization/import | `src/FaultWitness.Platform.Windows/WindowsDiagnosticsProvider.cs`; `src/FaultWitness.Platform.Windows/WindowsImportService.cs`; `src/FaultWitness.Platform.Windows/WerParser.cs` |
| What Changed | `src/FaultWitness.Platform.Windows/WindowsChangeHistoryProvider.cs`; `src/FaultWitness.Platform/ChangeHistoryEnricher.cs`; `src/FaultWitness.Core/ChangeCorrelator.cs`; display in `src/FaultWitness.App/MainWindow.Detail.cs` |
| Readiness / Inventory | `src/FaultWitness.Platform.Windows/WindowsDiagnosticReadiness.cs`; `src/FaultWitness.Platform.Windows/WindowsReadinessSources.cs`; `src/FaultWitness.Platform.Windows/WindowsSystemInventory.cs`; UI in `src/FaultWitness.App/MainWindow.Pages.cs` |
| Capture / Restore | `src/FaultWitness.App/CaptureWorkflow.cs`; `src/FaultWitness.App/MainWindow.Capture.cs`; `src/FaultWitness.Platform.Windows/CrashCaptureRegistry.cs`; `src/FaultWitness.ElevatedHelper/Program.cs` and `CaptureRequestProtocol.cs` |
| Storage / History / Journal | `src/FaultWitness.Storage/FaultWitnessStore.cs`; `src/FaultWitness.Storage/CaptureJournalStore.cs`; history UI in `src/FaultWitness.App/MainWindow.Pages.cs` |
| Export / privacy | `src/FaultWitness.App/MainWindow.Export.cs`; `src/FaultWitness.Export/ReportExporter.cs`; `src/FaultWitness.Core/SystemChangePrivacy.cs` |
| Theme / design tokens | `docs/design/faultwitness.tokens.json`; `src/FaultWitness.Design/DesignTokenCatalog.cs`; App resource bridge in `src/FaultWitness.App/MainWindow.cs` |
| Release ZIP layout | `scripts/Compose-ReleaseArtifact.ps1`; validation in `scripts/Test-ReleaseArtifact.ps1` |
