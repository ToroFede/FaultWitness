# FaultWitness product UX

## Presentation, not a second diagnostic engine

Core results, rule dispositions, confidence, temporal windows and incident identities are unchanged. The UI keeps every incident under **All**. Presentation priority is not a health score, failure count, or hardware verdict.

- **Needs attention:** at least one Significant finding with High severity and Strong/Moderate evidence.
- **Worth knowing:** an observed unclean shutdown (cause remains unknown); or a Significant Medium/High, Strong/Moderate finding with an exact recurring pattern supplied by Core; or an existing supported rule establishing a matching display-driver event, a repeated graphics/application/audio/WHEA/storage/service pattern, or exhaustion correlated with an application failure.
- **Background/context:** all remaining findings, including Expected, Suppressed, Supporting and Context; isolated moderate telemetry/application/audio signals; low-severity or limited-evidence patterns; a graphics timeout with only limited temporal app-crash context. A WER/Reliability mirror does not itself promote an occurrence.

There are no target percentages. A real issue can produce many relevant items. Counts refer to retained incident records, not proven independent failures. The recent list shows at most six higher-priority signature representatives; opening All still exposes every separate incident.

## Similar entries and report reuse

Cards show local timestamps including seconds. Core exact recurring patterns remain navigable as separate incidents. Shared nonempty report identifiers are a separate presentation relationship, scoped by platform and category. Such cards explicitly say that N records referencing one report do not establish N independent failures. Empty, all-zero and redacted identifiers do not form a relationship. No records are merged or deleted by the UI.

## Progressive disclosure

Home leads with exactly one dominant, timeframe-specific analysis action. Recent-period, around-time, and diagnostic-file workflows live together under Analyze; Incidents, History, and System are destinations, while Settings is visually separated. Recent analysis defaults to seven days; 24 hours, 30 days and a custom date interval are available. Around-time analysis uses local date/time and defaults to ±5 minutes. Operations expose stages, cancellation and safe errors. Processing runs away from the UI thread.

Incident cards show assessment, evidence strength and useful process/module/device context. Detail separates what happened, timeline, evidence, assessment, possible explanations, limitations and next step. Timeline records, raw XML, structured fields and source details are expandable. Source records and technical identifiers are not removed.

## Evidence and coverage

Observed, not observed and unknown evidence use words and symbols, not color alone. Observed records use existing provider-aware event descriptions. Unknown evidence names its source and available coverage state; completeness of another interval is never substituted completeness for this incident. Coverage includes System/Application logs, WER, Reliability and crash artifacts. Partial coverage explicitly means the whole requested interval cannot be confirmed.

## Import, support and privacy

Browse and drag/drop use the existing EVTX/WER/FaultWitness ZIP pipeline. Standalone JSON is not supported by that pipeline and is not advertised as importable. The UI accepts at most 20 files and 20,000 aggregate events, displays per-file status, rejects unsafe input and preserves partial imported coverage. Imported results are labelled as imported data, not as the current machine.

Support copy goes through a preview. Markdown, HTML, JSON and ZIP can be saved locally; no automatic sending is implemented. Default redaction covers profile paths, current computer name, IPv4 and account/serial fields as implemented by the existing exporter. Raw XML and dump files are excluded in the GUI. Users must review exports before sharing; redaction is not a guarantee that arbitrary diagnostic text contains no personal details. Large previews are explicitly limited to 24,000 characters; saving/copying keeps the complete selected scope.

## Settings and available information

System/Light/Dark themes and System default/English/Italian/Spanish/French/German/Portuguese/Russian/Polish languages switch at runtime. Views are recreated without restarting the process. Settings persist locally. SQLite schema v2 retains compact summaries, analysis type/period, coverage summary, duration when available, and presentation counts—but never raw log XML. Existing databases migrate without losing their older summaries; unavailable legacy metadata is labelled rather than fabricated. History supports reopening and copying the retained summary. Clear history uses a destructive-action treatment and confirmation, preserves language/theme, and never clears Windows logs or source files.

The System page uses existing inventory plus authoritative Windows product presentation: WMI `Win32_OperatingSystem.Caption`/`BuildNumber` and the system's `DisplayVersion` registry value when present. Raw NT version is advanced information; missing product data is not guessed from a build-number table. [Microsoft's WMI property documentation](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-operatingsystem).

Readiness checks existing sources, not computer health. Change history, dump configuration, free-space/page-file readiness and application dump configuration are explicitly unavailable in this pass. No additional collectors or configuration writers were added.

## Scale and accessibility

Incident lists are virtualized. Tests exercise 100, 1,000 and 5,000 records, priority/search filtering, bounded previews, small/medium/large logical widths, keyboard focus, control names, runtime language/theme switching, cancellation and error states. Main controls have accessible names; progress/status is announced to assistive technology. The canonical token and component/screen contracts live under `docs/design/` and are validated against the Avalonia resource bridge. These checks are not a full screen-reader, multi-monitor or high-DPI certification.
