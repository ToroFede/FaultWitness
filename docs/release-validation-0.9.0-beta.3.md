# Beta.3 candidate validation — 26 October 2026

Status: **HOLD — not approved for public release**. This record distinguishes executed checks from pending manual/security gates. Beta.2 remains the public release.

## Executed checks

| Check | Result and scope |
|---|---|
| Clean Release build | PASS, zero warnings/errors. |
| Full local suite | PASS, 1,100 tests including 576 UI tests. Ordinary suites include opt-in live tests that return without running unless enabled; live coverage below was exercised separately. |
| Real Windows inventory | PASS, 70 comparisons against independent CIM clients, registry/runtime values; Windows, CPU, usable RAM, GPU, disks, firmware and relevant drivers. Shared Windows providers limit the independence of this check; this is not physical hardware inspection. |
| Real evidence provenance | PASS, 25 sampled event anchors matched independently read channel/record ID, provider, event ID and timestamp. Findings retain causal-limit and provenance fields. Sampling does not establish correctness of every possible source or diagnosis. |
| Native end-to-end | PASS for a real seven-day analysis, collected inventory, nine Readiness sources, saved history, export preview and Windows save picker. Private records stayed local, outside the source repository. |
| Readiness | Ready and Limited states were observed with real providers. Source capability remains separate from PC health. |
| Import/export | Markdown in eight languages, HTML, JSON and support ZIP generated. A selected real incident round-tripped with bundle provenance and Partial imported coverage. The full large bundle was safely rejected by the existing import size bounds; the original full export was retained locally. |
| Redaction | Saved native export excluded the current account profile path and computer name. Regression tests cover account/address/raw XML/dump handling and malicious import inputs. This is not a guarantee that arbitrary diagnostic free text can never identify a person: preview inspection remains required. |
| Capture Preview and real UAC | PASS in network-disabled Windows Sandbox. Preview left the absent target key absent. Real secure-desktop UAC prompts were exercised with both refusal and consent. Configure wrote the expected three values; journal recorded the outcome; drift blocked Restore; successful Restore returned the guest registry to its initially absent state. No host Capture settings changed. |
| Partial-write rollback | PASS in a second network-disabled Sandbox. A test-only registry wrapper wrote only DumpType to a randomly named guest HKLM LocalDumps key, then injected UnauthorizedAccessException. The production LocalDumpCaptureEngine, WindowsLocalDumpRegistry read/restore methods, CaptureWorkflow, SQLite CaptureJournalStore and exit-protocol codec were exercised. The guest key was absent before and after; result was AccessDenied with rollback attempted/succeeded; journal recorded AutomaticComplete and the restored state. Injection exists only in the test harness; no failure switch or test code is in the shipping helper. |
| Responsive/localized UI | Suite covers eight languages, themes, narrow layouts, DPI scaling, focus, Reduced Motion, disclosure states, missing data and access/error fixtures. Native visual/material evidence from the approved redesign is retained locally; it does not replace a physical multi-monitor or human screen-reader test. |
| Clean Windows package | PASS in network-disabled Windows Sandbox without a system .NET installation: root launcher started the self-contained app, version matched, no helper started, graceful close succeeded. |
| Package audit | PASS, clean root, fixed-path launcher, helper payload/dependency identities, notices, file hashes and ZIP checksum. Two negative build tests rejected missing/stale helper payloads. ARM64 payload audit is build-only and does not establish ARM64 product support. |
| Dependency advisories | No vulnerable packages reported by the NuGet audit at validation time; this is a point-in-time advisory check. |
| Publication content | No detected credentials or current personal workspace paths in the sanitized publication tree; no tracked executable/archive/database/diagnostic binaries. Private test data and logs are excluded. |

## Performance and stability limits

The shipping self-contained executable was sampled without UI Automation attached: approximately 0.30% total-machine CPU over 40 seconds on a 24-logical-processor host, and it closed normally. Private memory changed from about 169 to 181 MiB during this short startup/idle sample; this alone is not evidence of a leak or proof of long-duration stability.

A native session with preserved real seven-day data on the Export page reached roughly one CPU core even without explicitly selecting that new process in the inspector. Managed stack samples identified Avalonia UI Automation `Navigate`/`GetFragmentRoot` calls. Resetting the inspection client, without restarting or changing the app/data, reduced the same process to zero sampled CPU seconds over 30 seconds; private memory was constant at approximately 1.33 GiB. The large real dataset and harness deserialization make this memory figure unsuitable as a normal startup baseline. Earlier redesign evidence also records inspection interference. It is not valid to report the attached session as ordinary idle performance, nor to declare assistive-technology performance verified. A human Narrator/performance check remains open. Short samples do not establish the absence of leaks; sustained/GPU/multi-monitor coverage is incomplete.

## Remaining manual gate

**Narrator walkthrough and audible announcements:** Narrator was started temporarily and the initial FaultWitness accessibility tree exposed its navigation and Home controls. The Computer Use helper could not activate the app window because the Narrator window ran at a higher Windows integrity level. No keyboard navigation, Capture confirmation, Export dialog, focus-return, or spoken announcement was therefore claimed as manually verified. The Narrator processes started for this check were closed afterward.

Manual confirmation still needed before release:

1. With Narrator on, move through Home, Analyze, System and Capture using Tab/Shift+Tab and arrow keys; confirm page names, control names/roles and focus order are spoken.
2. In Capture, inspect the executable field, run Preview, and confirm Narrator announces the target and read-only/unconfigured state. Open the Configure confirmation, verify dialog title, explanation, buttons and initial focus, then cancel; do not approve a host registry change for this check.
3. Navigate to Export, review the preview, open and close the save picker without saving, and confirm focus returns to the initiating control and status text is announced.
4. Listen for duplicate, missing or stale announcements after navigation and dialog close. This environment cannot capture or judge the host audio, so these audible checks must be confirmed by a person.

Do not merge or publish the release until this manual gate is completed and recorded. Keep the PR in draft; no beta.2 asset or tag is replaced.

## Diagnostic/source boundary

Relative to the public beta.2 baseline, the redesign does not change Core, Rules, platform collectors, Storage, Export semantics or privileged Capture workflow implementation. The new version changes product identity without changing journal schema. Linux/macOS remain architectural work, not supported desktop targets.

Local harness mistakes and their failed attempts are retained in private validation records. In particular, an initial uncaught validation assertion concerned an oversized export/import expectation; it was a harness failure, not evidence of a FaultWitness application crash. It was not removed from Windows diagnostic history.
