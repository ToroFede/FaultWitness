# FaultWitness 0.9.0-beta.2

This is a Windows 11 x64 **beta pre-release**. It remains unsigned; review source coverage and causal limits before acting on an assessment.

Changes since beta.1:

- Clearer supported diagnostic scope and completed-analysis results, with actual periods and visible coverage limits. Quiet results do not imply that the PC is healthy.
- More concrete incident identification: localized event type, recorded application/process/component, explicit unknown identity, readable source provenance and background-activity guidance.
- Reorganized Incident Detail keeps recorded facts and next steps ahead of interpretations, causal limits and technical investigation.
- Analyze contains **Analyze** and **Capture next crash**. System contains **Inventory** and **Diagnostic Readiness**, with independent Inventory card expansion.
- Clearer Capture presentation, command-local feedback, Light/Dark and responsive polish, and improvements across all eight languages.
- Complete migration of the UI to AXAML for maintainability while preserving diagnostic rules and Capture security boundaries.
- Visible guidance for the existing maximum analysis range of **90 days**.
- Clean-root portable packaging: open the root **FaultWitness.exe** and keep the complete **app/** folder beside it. No separate .NET installation is required.

FaultWitness reads supported Windows records and dump metadata; it does not analyze dump contents or establish root cause from temporal proximity. Capture configuration remains optional and administrator-approved. No telemetry or automatic upload.

Download **FaultWitness-0.9.0-beta.2-win-x64.zip** and verify it against the adjacent **SHA256.txt**.
