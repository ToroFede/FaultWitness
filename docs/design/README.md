# FaultWitness design system

`faultwitness.tokens.json` is the canonical visual value graph. It follows the DTCG group, `$type`, `$value`, and alias model. `FaultWitness.Design` resolves it and the Avalonia app consumes the resolved semantic resources directly; XAML/C# screens do not keep parallel color palettes.

`ui-contract.json` defines when semantic components may be used. `screen-specs.json` is the implemented information architecture and responsive contract. `ux-acceptance.json` separates honest automated checks from semi-automatic and human visual review. Its validator runs in `FaultWitness.Design.Tests` and blocks missing references, cycles, theme drift, contrast failures, raw styling-color drift, and contract inconsistencies.

Breakpoints use effective pixels: small through 640, medium 641–1007, large from 1008. The 4 epx spacing ramp is the default; one-pixel borders and the three-pixel indeterminate progress track are framework/optical exceptions.

Light and Dark values are semantic aliases. “Ready” means diagnostic capability or successful action, never overall system health. Diagnostic states always include text; color is supplementary.

Beta UX polish keeps page guidance at body size, section headings at the section-title size, and timestamps/technical metadata at caption size. Compact incident rows place timestamps below titles. Buttons wrap long translated labels, share token radii, and use semantic accent hover/pressed colors; Fluent keyboard focus remains intact. Reversible Configure/Restore commands use the secondary action's state-changing variant, with explicit wording and existing confirmation. Red danger styling remains reserved for destructive commands.

Capture presents the application name, Preview, privacy/storage/administrator implications, then Configure. Raw configuration values are collapsed under Technical details; capture status, limitations, and Restore guidance must remain available without suggesting that configured means captured. History and readiness empty states use their own next-step guidance. What Changed places its non-causal explanation before the change list.

Pass 2A authors the shell and Incident Detail in AXAML. Detail uses one reading column at every breakpoint, replacing the stale 65/35 contract. Its identity, next step, interpretation and causal limits are visible, followed by any material source limitation and all grouped evidence. Timeline, changes, optional patterns/hypotheses/other steps, full coverage and technical records are closed initially. The next step is the strongest surface; other sections are not equal cards. The forty-case synthetic visual matrix and focused hierarchy tests support review; they do not substitute for a real user's comprehension check. Remaining pages still use C# builders pending Pass 2B.
