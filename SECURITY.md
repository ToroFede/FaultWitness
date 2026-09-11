# Security policy

FaultWitness 0.9.0-beta.1 is a Windows 11 x64 beta. Treat imported diagnostic files and crash dumps as untrusted. WER imports are bounded and imported content is not executed. Do not run the main application elevated.

Crash dumps can contain private process memory, including credentials or document contents. Keep dumps local; do not attach raw dumps or unredacted diagnostic exports to public reports. The normal support export excludes dump bytes and redacts personal data by default, but review every export before sharing.

The separate elevated helper accepts only the schema-versioned semantic crash-capture configuration and Restore requests described in [the capture threat model](docs/capture-next-crash-security.md). It validates requests independently, compares current state, verifies writes, and never exposes a generic registry, file, or command interface. Each invocation requires administrator permission; command-line arguments are not caller authentication. Capture configuration is recorded in the local Action Journal; it does not provide cryptographic provenance or protect against a same-user process replacing an unsigned development binary.

When GitHub Private Vulnerability Reporting is enabled for this repository, use that private reporting flow. If it is not enabled, contact the maintainer through a private repository channel and do not publish exploit details, raw dumps, or unredacted exports in an issue or discussion.
