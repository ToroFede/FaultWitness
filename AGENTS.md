# FaultWitness contributor notes

- UI changes must follow `docs/design/*`.
- `docs/design/faultwitness.tokens.json` is the design-token source of truth.
- `ui-contract.json` defines component semantics; `screen-specs.json` defines information hierarchy; `ux-acceptance.json` defines acceptance checks.
- Do not bypass semantic tokens without a documented, narrow exception.
- Pure UI work must not change diagnostic semantics in `FaultWitness.Core` or `FaultWitness.Rules`.
- After UI changes, run affected Design/UI/localization tests; run the full regression suite at integration/final gates.

## Codex efficiency

Use model/token budget conservatively without compromising requested quality. Follow `docs/engineering-efficiency.md` for the full policy.

- Reuse established context and repository documentation.
- Read/search files surgically; avoid repeatedly loading whole files.
- Run focused tests during development and the full suite at integration/final gates.
- Keep tool/chat output concise; store detailed reports in files at the requested location.
- Avoid duplicate web research and redundant agents; delegate only bounded work whose benefit exceeds coordination cost.
- When model selection is available, use cheaper models for reliable mechanical work, medium reasoning for clear implementation, and stronger reasoning only for genuinely complex work.
- If usage/quota information is available, consider it near the start and before expensive phases; do not repeatedly query it.
- Do not rewrite working code unnecessarily.
- Stop once all requested acceptance criteria are satisfied.
- Never trade correctness, security, diagnostic reliability, tests, or completeness for token savings.
