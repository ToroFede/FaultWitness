# Synthetic diagnostic corpus

56 structured scenarios in `scenarios/`. All timestamps, identifiers, applications, modules, paths and devices are invented. No local Event Log, WER, Reliability or dump contents are copied here.

Each JSON file contains Name, Description, Events (the NormalizedEvent model), and Coverage (SourceCoverage with channel and examined UTC interval). String enums make fixtures readable. Empty events are intentional for clean_system. Coverage is synthetic test input, not a claim about a real collector.

`catalog.json` lists the corpus. `rule-expectations.json` maps all 37 rules to positive and negative scenarios. Family-specific xUnit methods expose each expected outcome; shared loading/assertion helpers only handle plumbing. Every scenario is referenced by a test. Tests mutate selected timestamps or identity fields to exercise boundaries without duplicating whole fixture files.

Time convention: closed, anchor-based windows (<=120 or <=300 seconds), with rule-specific direction. No chaining. Occurrence deduplication is separate: matching report/source-record identity is capped at 30 seconds; a conservative cross-representation signature fallback is capped at 2 seconds and rejects conflicting known PIDs. Same-source records without shared report identity remain separate. Recurrence across days links separate incidents.

Explicit ModuleTrust=VerifiedThirdParty and ModuleRole=APO are synthetic evidence assertions. Neither a filename nor an arbitrary path supplies this trust automatically. Likewise Vendor is supplied explicitly; an isolated vendor event does not prove a TDR.

WER/ZIP/XML malicious-input cases are generated inline by Windows tests. Their size limits are reduced for efficient tests; the same production checks are exercised. A safe empty EVTX is generated at runtime using an impossible EventRecordID query, then removed. The access-denied test modifies only its own temporary file ACL, restores it in finally and deletes the file.

Run `dotnet test` from the repository root. See [rule coverage](../docs/rule-coverage.md) for the full audit, rule contracts and validation limits.
