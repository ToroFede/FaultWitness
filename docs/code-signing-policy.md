# Code signing policy

Status: FaultWitness 0.9.0-beta.1 is an intentionally unsigned public beta. Trusted code signing is intended for a future release when approved signing infrastructure becomes available. This policy does not claim that any provider, certificate, or signing integration has been approved or activated.

## Initial beta exception

The Windows binaries in FaultWitness 0.9.0-beta.1 are distributed without a trusted Windows code-signing certificate. Windows may therefore show “Unknown publisher” and/or a Microsoft Defender SmartScreen warning. This does not mean Windows has cryptographically verified the publisher. The release provides a SHA-256 checksum for verifying the package bytes; a matching checksum does not verify the publisher's identity. Users should keep normal Windows security protections enabled.

## Scope

Trusted Authenticode signing is intended for first-party Windows release executables produced by the official GitHub Actions release workflow:

- FaultWitness.exe
- helper/FaultWitness.ElevatedHelper.exe

Third-party binaries, including the self-contained .NET runtime and NuGet dependencies, are not in the planned project-certificate scope. Managed assemblies and resource assemblies are also outside the initial scope unless this policy is reviewed before a future signing configuration is approved.

## Team roles

The project is maintained by Federico Santoro (GitHub: [@ToroFede](https://github.com/ToroFede)), the repository owner.

- **Author / committer:** Federico Santoro.
- **Reviewer:** Changes are reviewed before entering a signed release branch or tag; contributions from other people are reviewed before merge.
- **Signing approver:** The repository owner approves each future release signing request.

All project members with repository or signing-provider access must use multi-factor authentication. A signing request is never approved solely because a build succeeds.

## Release provenance and approval

Only a clean, reproducible Windows x64 build from the public [ToroFede/FaultWitness](https://github.com/ToroFede/FaultWitness) repository may be submitted for signing. The official release workflow must run on GitHub-hosted runners, record the source commit, audit the unsigned payload, and provide provenance to the selected trusted signing provider for origin verification. A maintainer manually approves each signing request after confirming the intended source revision, release scope, and artifact configuration.

For future releases that use this policy, signed production artifacts are limited to supported public releases and public beta/pre-release candidates from an explicitly approved source commit. The immutable release tag is created only after signing and final validation, and must identify that same commit. Pull-request, development, local, and unreviewed builds must not receive production signatures; a workflow re-run requires a fresh manual review and approval. The initial 0.9.0-beta.1 release is the documented unsigned exception above.

When trusted signing is implemented, release materials will identify the actual signing provider and certificate accurately. No provider or certificate attribution is claimed before signing is active.

## Key protection, timestamping, and verification

No exportable private signing key is stored in this repository, on a developer workstation, or in GitHub. If a managed signing service is used, its protected key storage retains the private key. The release workflow uses only the least-privilege credential needed to submit an approved signing request.

Future signatures must use SHA-256 Authenticode signing and a trusted RFC 3161 timestamp. Before packaging a signed release, the workflow verifies the signer, certificate chain, timestamp, and signature validity using Windows Authenticode rules. For every release, including the current unsigned beta, the package manifest and SHA-256 checksum cover the published bytes. A checksum verifies package integrity against the published checksum; it does not authenticate the publisher.

## Integrity, incidents, and revocation

If a signing credential, signing workflow, signed artifact, or release provenance is suspected to be compromised, the maintainer will pause signing and publication, investigate, notify the signing provider as applicable, revoke or replace affected signatures/certificates where possible, and publish corrected release guidance.

## Privacy

Signing processes must not transfer project, contributor, or user information to another networked system except for information necessary to the explicitly approved signing operation. See the [README privacy section](../README.md#privacy-and-exports) for the current beta behavior.
