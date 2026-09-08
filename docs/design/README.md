# FaultWitness design system

`faultwitness.tokens.json` is the canonical visual value graph. It follows the DTCG group, `$type`, `$value`, and alias model. `FaultWitness.Design` resolves it and the Avalonia app consumes the resolved semantic resources directly; XAML/C# screens do not keep parallel color palettes.

`ui-contract.json` defines when semantic components may be used. `screen-specs.json` is the implemented information architecture and responsive contract. `ux-acceptance.json` separates honest automated checks from semi-automatic and human visual review. Its validator runs in `FaultWitness.Design.Tests` and blocks missing references, cycles, theme drift, contrast failures, raw styling-color drift, and contract inconsistencies.

Breakpoints use effective pixels: small through 640, medium 641–1007, large from 1008. The 4 epx spacing ramp is the default; one-pixel borders and the three-pixel indeterminate progress track are framework/optical exceptions.

Light and Dark values are semantic aliases. “Ready” means diagnostic capability or successful action, never overall system health. Diagnostic states always include text; color is supplementary.
