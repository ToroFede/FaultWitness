# Security policy

Treat imported diagnostic files as untrusted. WER imports are bounded and imported content is not executed. Do not run the main application elevated.

The separate elevated helper accepts only the schema-versioned semantic crash-capture configuration and restore requests described in [the capture threat model](docs/capture-next-crash-security.md). It validates requests independently, compares current state, verifies writes and never exposes a generic registry, file or command interface. Every invocation requires administrator permission; command-line arguments are not caller authentication.

Crash dumps can contain private process memory. Keep them local; do not attach raw dumps or unredacted diagnostic exports to public reports. Report vulnerabilities privately to the maintainer.
