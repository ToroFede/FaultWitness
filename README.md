# FaultWitness

FaultWitness began with a simple problem: Windows often records useful crash information, but that information is spread across several tools and rarely arrives with the context needed to investigate it.

It reads selected local diagnostic sources, normalizes the records, correlates related records into incidents, and reports what the evidence supports. It separates observed facts, correlations, interpretations, hypotheses, and conclusions that cannot be established.

FaultWitness does not diagnose a failing power supply from Kernel-Power 41, a defective CPU from a WHEA event, or a root cause from a faulting module name. It has no account, telemetry, analytics, cloud processing, or background service.

## Current support

The collector targets Windows 11 and reads the System and Application Event Logs, Windows Error Reporting archives where accessible, and dump artifact locations. The Core, rule, storage, export, and localization projects are platform-neutral; future collectors can be implemented without placing platform APIs in the Core.

The desktop shell now provides prioritized Overview/Incidents, recent and around-time analysis, evidence/coverage detail, local import/export, source readiness, system information and runtime language/theme settings. All diagnostic records remain accessible. See [the UX behavior and limitations](docs/ux.md).

## Build and test

Install .NET 10 SDK, then run:

```powershell
dotnet restore FaultWitness.slnx
dotnet build FaultWitness.slnx --no-restore
dotnet test FaultWitness.slnx --no-build
dotnet run --project src/FaultWitness.App
dotnet run --project src/FaultWitness.Cli -- scan --last 7d
```

## Privacy and exports

Compact scan summaries are stored in SQLite under the local application-data directory. Raw Event XML is not retained. Markdown, HTML, JSON, and ZIP support exports redact user-profile paths, the local computer name, and IPv4 addresses by default. Raw XML is opt-in; dumps are excluded.

## Limitations

This private release candidate implements an initial rule catalog and conservative collectors. It does not analyze dump contents, make network updates, collect all reliability/change-history sources, or provide Linux/macOS collectors.
