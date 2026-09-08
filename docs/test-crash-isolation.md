# Test crashes and live diagnostic evidence

Windows Error Reporting and Application Error records describe real process failures, including test-host failures. Live scans retain all readable, in-range reports. Never delete reports to obtain a clean validation result, exclude executable prefixes, or change diagnostic severity, confidence, correlation, or priority to reduce development noise. FaultWitness application crashes remain visible.

The presentation may identify an exact known test executable filename as context. This is a filename hint, not proof of publisher, origin, or harmlessness. Keep the original process, evidence, and priority visible in both development and release builds. Do not introduce a development-only scan filter.

Prevent contamination at its source: fix unintended test crashes and keep bounded regression coverage for selection/rebuild lifecycles. Run intentionally crashing tests and investigations that can terminate the host in a disposable Windows VM or disposable CI worker. A temporary application data directory alone does not isolate machine-wide WER or event logs. Preserve relevant failure evidence before disposing of that environment; never disable WER globally as a test workaround.

Ordinary non-crashing tests may run locally. If an unexpected host crash occurs, treat it as a failed gate, preserve its evidence, and move further crash reproduction to the isolated environment. Do not deliberately reproduce a historical stack overflow on a user's workstation. A later live scan on that workstation can truthfully include its historical test failures until they age outside the requested interval; explain this context rather than hiding them.

The September 2026 History regression validates bounded selection and detail updates. Without a retained managed stack from the historical WER reports, it cannot establish the exact cause of those archived crashes.
