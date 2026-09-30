# Contributing

- Keep platform-specific APIs inside their platform project.
- Rules must be provider-aware and include a false-positive contract.
- Do not add user-facing diagnostic prose to rule logic.
- Follow the design tokens and UI contracts under docs/design/ for interface changes.
- Start with [docs/architecture.md](docs/architecture.md) for system flow and [src/README.md](src/README.md) for project locations.
- Make focused, reviewable changes and preserve unrelated work.
- Start with the narrowest relevant checks; run broader validation when behavior changes or a release gate requires it.
- Keep public diagnostic language evidence-led and distinguish correlation from causation.
