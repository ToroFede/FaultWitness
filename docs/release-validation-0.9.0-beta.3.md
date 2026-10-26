# Beta.3 candidate validation — 26 October 2026

Status: **VALIDATED FOR BETA.3 PRE-RELEASE**. Automated checks and manual acceptance are recorded separately. Beta.1 and beta.2 remain unchanged.

## Executed checks

| Check | Result and scope |
|---|---|
| Clean Release build | PASS, zero warnings/errors. |
| Full local suite | PASS, 1,101 tests including 577 UI tests. Ordinary suites include opt-in live tests that return without running unless enabled; live coverage below was exercised separately. |
| Export keyboard focus | PASS, read-only preview lines do not create per-line Tab stops; regression test verifies Tab from export scope reaches the first action. |
| Real Windows inventory | PASS, 70 comparisons against independent CIM clients, registry/runtime values; Windows, CPU, usable RAM, GPU, disks, firmware and relevant drivers. Shared Windows providers limit the independence of this check; this is not physical hardware inspection. |
| Real evidence provenance | PASS, 25 sampled event anchors matched independently read channel/record ID, provider, event ID and timestamp. Findings retain causal-limit and provenance fields. Sampling does not establish correctness of every possible source or diagnosis. |
| Native end-to-end | PASS for a real seven-day analysis, collected inventory, nine Readiness sources, saved history, export preview and Windows save picker. Private records stayed local, outside the source repository. |
| Readiness | Ready and Limited states were observed with real providers. Source capability remains separate from PC health. |
| Import/export | Markdown in eight languages, HTML, JSON and support ZIP generated. A selected real incident round-tripped with bundle provenance and Partial imported coverage. The full large bundle was safely rejected by the existing import size bounds; the original full export was retained locally. |
| Redaction | Saved native export excluded the current account profile path and computer name. Regression tests cover account/address/raw XML/dump handling and malicious import inputs. This is not a guarantee that arbitrary diagnostic free text can never identify a person: preview inspection remains required. |
| Capture Preview and real UAC | PASS in network-disabled Windows Sandbox. Preview left the absent target key absent. Real secure-desktop UAC prompts were exercised with both refusal and consent. Configure wrote the expected three values; journal recorded the outcome; drift blocked Restore; successful Restore returned the guest registry to its initially absent state. No host Capture settings changed. |
| Partial-write rollback | PASS in a second network-disabled Sandbox. A test-only registry wrapper wrote only DumpType to a randomly named guest HKLM LocalDumps key, then injected UnauthorizedAccessException. The production LocalDumpCaptureEngine, WindowsLocalDumpRegistry read/restore methods, CaptureWorkflow, SQLite CaptureJournalStore and exit-protocol codec were exercised. The guest key was absent before and after; result was AccessDenied with rollback attempted/succeeded; journal recorded AutomaticComplete and the restored state. Injection exists only in the test harness; no failure switch or test code is in the shipping helper. |
| Responsive/localized UI | Suite covers eight languages, themes, narrow layouts, DPI scaling, focus, Reduced Motion, disclosure states, missing data and access/error fixtures. Native visual/material evidence from the approved redesign is retained locally; the user-confirmed Narrator walkthrough is recorded below. This does not replace a physical multi-monitor test. |
| Clean Windows package | PASS for network-disabled clean-root extraction and launcher startup; exact version matched and the app process appeared without an elevated helper. The latest exact-package smoke ran in a non-interactive System session and did not measure window-close behavior; interactive close was verified on the same application source. |
| Package audit | PASS, clean root, fixed-path launcher, helper payload/dependency identities, notices, file hashes and ZIP checksum. Two negative build tests rejected missing/stale helper payloads. ARM64 payload audit is build-only and does not establish ARM64 product support. |
| Dependency advisories | No vulnerable packages reported by the NuGet audit at validation time; this is a point-in-time advisory check. |
| Publication content | No detected credentials or current personal workspace paths in the sanitized publication tree; no tracked executable/archive/database/diagnostic binaries. Private test data and logs are excluded. |

## Performance and stability limits

The shipping self-contained executable was sampled without UI Automation attached: approximately 0.30% total-machine CPU over 40 seconds on a 24-logical-processor host, and it closed normally. Private memory changed from about 169 to 181 MiB during this short startup/idle sample; this alone is not evidence of a leak or proof of long-duration stability.

A native session with preserved real seven-day data on the Export page reached roughly one CPU core even without explicitly selecting that new process in the inspector. Managed stack samples identified Avalonia UI Automation `Navigate`/`GetFragmentRoot` calls. Resetting the inspection client, without restarting or changing the app/data, reduced the same process to zero sampled CPU seconds over 30 seconds; private memory was constant at approximately 1.33 GiB. The large real dataset and harness deserialization make this memory figure unsuitable as a normal startup baseline. Earlier redesign evidence also records inspection interference. The user has separately confirmed that the live Narrator walkthrough worked; no performance benchmark with Narrator running was performed. Short samples do not establish the absence of leaks; sustained/GPU/multi-monitor coverage is incomplete.

## Manual accessibility walkthrough — user confirmation

On 26 October 2026, the user reported completing a live Windows Narrator walkthrough and confirmed that navigation, spoken announcements, focus, Capture dialogs, Preview and Export worked correctly, with no blocking issue observed. This is explicitly **human-reported manual validation**, not an automated test result and not independently measured audio evidence from the validation tooling.

The automated suite separately verifies keyboard focus order, language coverage and accessibility-related UI behavior. It does not claim to verify Narrator audio.
## Diagnostic/source boundary

Relative to the public beta.2 baseline, the redesign does not change Core, Rules, platform collectors, Storage, Export semantics or privileged Capture workflow implementation. The new version changes product identity without changing journal schema. Linux/macOS remain architectural work, not supported desktop targets.

Local harness mistakes and their failed attempts are retained in private validation records. In particular, an initial uncaught validation assertion concerned an oversized export/import expectation; it was a harness failure, not evidence of a FaultWitness application crash. It was not removed from Windows diagnostic history.
