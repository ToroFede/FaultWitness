# UI redesign — phase 1 / visual approval checkpoint

Baseline:9ca3181bd89198fc159c83dc306dbeefabaf18ce, clean main, branch redesign/home-analyze-incident-detail. No AGENTS.md found in repository/ancestors. Existing Avalonia12.1.1, .NET10/MVVM and AXAML ownership retained. Reference images were not present in the received message; navy/blue/minimal direction follows the supplied text.

Preserve: canonical DTCG token catalog and contrast tests; localized presentation projections; cached pages and control names; evidence/interpretation/limits ordering; single live status owner; actual running/cancel/error states; all Capture/Restore/admin/export/diagnostic rules. No new telemetry, scores or diagnostic data. No packaging/release changes or push.

Improve: fragmented Home entry actions; flat summary counts; Analyze form grouping; dense incident identity/evidence hierarchy; weak surface hierarchy; missing motion preference. Reuse SurfaceCard, existing semantic components and shared opt-in redesign styles. Foundation palette/radii may flow to existing components; other page layouts are deliberately not redesigned before approval. Settings receives only accessibility/backdrop preferences.

Baseline build passed without warnings. Baseline tests have one existing failure on 2026-10-25: CommandFeedbackTests assumes two local calendar days always equal48hours, including the Europe/Rome DST transition. This is not a new UI regression; diagnostic time-range logic is unchanged.

Motion uses official Avalonia property transitions and a single incoming-page opacity animation, never overlapping old/new interactive pages or layout geometry animations. Durations are token-driven120–180ms. Reduced Motion is explicit and persisted; Avalonia's current public platform-settings API does not expose a portable OS reduced-motion preference. Busy text/cancellation remains available with animated progress hidden in reduced mode. Optional native Mica requests use Avalonia capability fallback; opaque solid surfaces remain default.

Real native-window review uses the repository's existing IncidentDetailDesignData fixture in a separate in-memory host. Fixture screenshots are explicitly labeled; no synthetic diagnostics are inserted into the product or stored as real findings. Test-run logs/screenshots live outside the repository under FaultWitness-Redesign-Review.

Review before extending: navy/blue direction, hero/summary cards, grouped analysis workflow, evidence-first incident hierarchy, density at560/1280effective pixels, light/dark/system, keyboard/focus and motion. Human screen-reader comprehension and other OS compositor checks remain manual/platform acceptance, not claimed from headless tests.

Official APIs: https://docs.avaloniaui.net/docs/graphics-animation/control-transitions ; https://docs.avaloniaui.net/docs/graphics-animation/animations ; https://docs.avaloniaui.net/docs/services/platform-settings .

## Implemented checkpoint

Reusable SurfaceCard and MetricCard own their themes; the metric template explicitly preserves surface/padding/radius across cached navigation and Light/Dark changes. All1067tests pass, including13new redesign cases. The initial DST-only test failure was corrected in its assertion using the local UTC-offset change, not in production range logic. Existing translations are byte-value-equivalent; only two preference labels were added in eight locales. Native screenshots demonstrate both themes,560epx layout, actual page navigation and mouse/Space disclosure interaction, using the pre-existing labeled synthetic fixture and no Windows collection.

Review artifacts: the locally retained phase 1 review and validation records (not part of the public repository). The source is intentionally uncommitted and awaits visual approval. No further page-layout rollout. Native UI Automation profiling exposed high CPU in the original baseline too; its underlying cause is not conclusively isolated and is recorded as an instrumentation/platform limitation, not a claim of idle performance. Human screen-reader, physical multi-monitor DPI and supported-compositor checks remain pre-release manual acceptance.
