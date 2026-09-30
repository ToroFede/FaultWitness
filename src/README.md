# Source projects

Start with [the architecture map](../docs/architecture.md) for the current dependency graph, data flow, and change locations.

Each directory under `src/` contains one production project:

- `FaultWitness.App` — Avalonia desktop app and Windows composition.
- `FaultWitness.Core` — neutral diagnostic and domain semantics.
- `FaultWitness.Platform` — neutral collection contracts and enrichment.
- `FaultWitness.Platform.Windows` — Windows collection, import, and OS-specific behavior.
- `FaultWitness.Rules` — provider-aware diagnostic rules.
- `FaultWitness.Storage` — SQLite history and Action Journal.
- `FaultWitness.Export` — shared report and summary exporters.
- `FaultWitness.Localization` — shared localized resources.
- `FaultWitness.Design` — design-token catalog and validation.
- `FaultWitness.Cli` — Windows command-line entry point.
- `FaultWitness.ElevatedHelper` — bounded, one-shot privileged helper process.

Project and directory names match. The repository keeps production projects under `src/`, tests under `tests/`, product and contributor material under `docs/`, and build/release tooling under `scripts/`.

Inside `FaultWitness.App`, start at `Views/MainWindow.axaml` for the shell. `Views/Pages/` contains the AXAML implementations for Home, Analyze, Incidents, History, Incident Detail, System/Inventory, Diagnostic Readiness, Settings, Export/Support and Capture. `Views/Components/` contains shared semantic controls, `Styles/SharedStyles.axaml` owns shared styling, and `Presentation/` projects existing state for bindings. `MainViewModel.cs` coordinates application state and services. Capture is hosted inside System through `CaptureView.axaml`; `CapturePresentation.cs` projects existing workflow state, `CaptureRestoreCard.axaml` displays journal/recovery entries and `CaptureConfirmationWindow.axaml` owns the explicit confirmations. `MainWindow.Capture.cs` forwards those events; safety decisions remain in `CaptureWorkflow.cs` and the Windows/helper boundary. All production page/shell and confirmation layout is AXAML. C# selects hosts, adjusts responsive properties and binds runtime state; it does not construct page layouts.
