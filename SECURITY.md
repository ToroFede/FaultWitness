# Security policy

Treat imported diagnostic files as untrusted. The current implementation limits WER input to 1 MiB and 2,000 lines; it does not execute imported content. Do not run the main application elevated. The separate elevated helper accepts only a validated executable filename and the explicit LocalDumps enable/restore commands.

Please report vulnerabilities privately to the maintainer. Do not include raw dumps or unredacted diagnostic exports in public reports.
