# Code signing policy

Status: SignPath Foundation onboarding is pending; no subscription or certificate approval is claimed. The first Windows 11 x64 beta remains a private GitHub Draft release until trusted signing and final validation are complete. The Foundation must confirm eligibility for a first release that is not yet publicly downloadable.

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

Signed production artifacts are limited to supported public releases and public beta/pre-release candidates from an explicitly approved source commit. The immutable release tag is created only after signing and final validation, and must identify that same commit. Pull-request, development, local, and unreviewed builds must not receive production signatures; a workflow re-run requires a fresh manual review and approval.

Upon acceptance, the signing attribution will be: Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

Before signing is enabled, the SignPath artifact configuration must enforce the product name `FaultWitness` for all signed binaries and an identical product version across each build. Configuration and build provenance remain subject to SignPath's approval; this policy does not claim that integration is already active.

## Key protection, timestamping, and verification

No exportable private signing key is stored in this repository, on a developer workstation, or in GitHub. When SignPath is used, its managed HSM retains the private key. The release workflow uses only the least-privilege credential needed to submit the approved signing request.

Each signature must use SHA-256 Authenticode signing and a trusted RFC 3161 timestamp. Before packaging, the workflow verifies the signer, certificate chain, timestamp, and signature validity using Windows Authenticode rules. The release package is composed only after verification; its manifest and checksum then cover the signed bytes.

## Integrity, incidents, and revocation

The published release contains a manifest and SHA-256 checksum for the final package. Users should verify the package checksum and Authenticode signature before use. If a signing credential, signing workflow, signed artifact, or release provenance is suspected to be compromised, the maintainer will pause signing and publication, investigate, notify SignPath as applicable, revoke or replace affected signatures/certificates where possible, and publish a corrected release with incident guidance.

## Privacy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it. See the [README privacy section](../README.md#privacy-and-exports) for the current beta behavior.
