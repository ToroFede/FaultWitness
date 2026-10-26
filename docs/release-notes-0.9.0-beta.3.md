# FaultWitness 0.9.0-beta.3

FaultWitness 0.9.0-beta.3 is an unsigned Windows x64 pre-release for evidence-first review of supported Windows stability records.

## Highlights

- Refined the desktop UI across Home, Analyze, Incident Detail, System, Diagnostic Readiness, Capture, History, Settings and Export.
- Improved System inventory layout, keyboard focus order, accessibility, responsive layouts and the existing eight languages.
- Kept Capture Preview read-only; Configure and Restore retain explicit confirmation, elevation, journal, drift safeguards and rollback behavior.
- Verified partial-write rollback and journal reporting under controlled isolated failure tests; no failure injection is included in the product.
- Preserved evidence provenance and the distinction between facts, correlations, interpretations and hypotheses.

## Compatibility and limitations

- Supported desktop target: Windows 11 on x64 Intel/AMD PCs.
- Windows ARM64, Linux and macOS are not supported releases.
- The package is unsigned. Windows may show Unknown publisher or SmartScreen warnings; SHA-256 checks package integrity, not publisher identity.
- FaultWitness reviews supported diagnostic records; it is not a general PC health checker and does not establish root cause beyond available evidence.
- Import of a complete support bundle is bounded; an entry over the 8 MiB import limit is rejected. Smaller or selected-incident exports remain available.

Download the Windows package from the [0.9.0-beta.3 pre-release](https://github.com/ToroFede/FaultWitness/releases/tag/v0.9.0-beta.3) and verify it with the adjacent `SHA256.txt` asset.
