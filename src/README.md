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

Inside `FaultWitness.App`, start at `Views/MainWindow.axaml` for the shell, `Views/Pages/IncidentDetailView.axaml` for Incident Detail, `Views/Components/` for semantic controls, `Styles/SharedStyles.axaml` for shared styling, and `Presentation/IncidentDetailPresentation.cs` for the detail projection. `MainViewModel.cs` still coordinates application state and services. Other page layouts remain in the root `MainWindow.*.cs` partials until Pass 2B; do not duplicate them when migrating a page.
