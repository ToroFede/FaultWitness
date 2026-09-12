# Code signing policy

## Scope

FaultWitness will request Authenticode signing only for first-party Windows release executables produced by the official GitHub Actions release workflow:

- `FaultWitness.exe`
- `helper/FaultWitness.ElevatedHelper.exe`

Third-party binaries, including the self-contained .NET runtime and NuGet dependencies, are not signed with a FaultWitness project certificate. Managed assemblies and resource assemblies are not in the initial signing scope unless this policy is reviewed and updated before a later signing configuration is approved.

## Team roles

The project is maintained by Federico Santoro (GitHub: [@ToroFede](https://github.com/ToroFede)), the repository owner.

- **Author / committer:** Federico Santoro.
- **Reviewer:** Federico Santoro reviews changes before they enter a signed release branch or tag; contributions from other people are reviewed before merge.
- **Signing approver:** Federico Santoro approves each release signing request.

All project members with repository or SignPath access must use multi-factor authentication. A signing request is never approved solely because a build succeeds.

## Release provenance and approval

Only a clean, reproducible Windows x64 build from the public [`ToroFede/FaultWitness`](https://github.com/ToroFede/FaultWitness) repository may be submitted. The release workflow must run on GitHub-hosted runners, record the source commit, audit the unsigned payload, and submit the workflow artifact to SignPath for origin verification. A maintainer manually approves each signing request after confirming the intended source revision, release scope, and artifact configuration.

Signed production artifacts are limited to supported public releases and public beta/pre-release builds from an explicitly approved release tag. Pull-request, development, local, re-run, and unreviewed builds must not receive production signatures.

Free code signing provided by SignPath.io, certificate by SignPath Foundation.

## Key protection, timestamping, and verification

No exportable private signing key is stored in this repository, on a developer workstation, or in GitHub. When SignPath is used, its managed HSM retains the private key. The release workflow uses only the least-privilege credential needed to submit the approved signing request.

Each signature must use SHA-256 Authenticode signing and a trusted RFC 3161 timestamp. Before packaging, the workflow verifies the signer, certificate chain, timestamp, and signature validity using Windows Authenticode rules. The release package is composed only after verification; its manifest and checksum then cover the signed bytes.

## Integrity, incidents, and revocation

The published release contains a manifest and SHA-256 checksum for the final package. Users should verify the package checksum and Authenticode signature before use. If a signing credential, signing workflow, signed artifact, or release provenance is suspected to be compromised, the maintainer will pause signing and publication, investigate, notify SignPath as applicable, revoke or replace affected signatures/certificates where possible, and publish a corrected release with incident guidance.

## Privacy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it. See the [README privacy section](../README.md#privacy-and-exports) for the current beta behavior.
