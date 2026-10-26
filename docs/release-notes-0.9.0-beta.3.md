# FaultWitness 0.9.0-beta.3 — candidate

This candidate is not yet a public release. Beta.2 assets and its release tag remain unchanged.

## Desktop redesign

- Consistent navy/blue surfaces, reusable components, clearer typography and restrained native materials with an opaque fallback.
- Refined Home, Analyze, Incident Detail, System, Diagnostic Readiness, Capture, History, Settings and Export/Support views.
- Discrete navigation and control transitions, Reduced Motion, Light/Dark/System themes and responsive layouts across the eight existing languages.
- Independent auto-height System columns: expanding storage or drivers no longer reserves empty space in the neighboring column. Narrow windows use one column and preserve disclosure state and logical reading order.
- More readable export preview, without changing the diagnostic meaning or the exported content.

The diagnostic rules, evidence/causality boundaries, Capture/Restore safeguards and local-first privacy model remain in place. Windows 11 x64 is the supported desktop target. Linux/macOS require real platform implementations and are not supported releases.

## Distribution

The Windows package retains the clean root (`FaultWitness.exe`, `README.md`, `QUICKSTART.md`, `app/`), fixed-path unelevated launcher, self-contained .NET runtime and isolated helper payload. Package validation now derives the version from `Directory.Build.props`, rather than accepting only beta.2.

This candidate remains unsigned. Checksums verify package bytes; they do not authenticate the publisher. Keep normal Windows protections enabled.

## Release gates

A public release requires passing CI and package audits, an isolated clean-Windows launch, a real UAC Configure/Restore test with journal/rollback/drift verification in an isolated Windows VM, the native save picker flow and a manual Narrator check. Unexecuted gates must remain explicitly unverified; passing fixture/unit tests does not substitute for them.

See the [candidate validation record](release-validation-0.9.0-beta.3.md) for executed checks and the blocking gates that remain open.

ZIP import retains its safety bounds. A large exported support summary may exceed the 8 MiB per-entry import limit; a selected-incident export can be used instead. This does not remove the full local export or change diagnostic results.
