# Engineering budget policy

This standing policy applies to work in the FaultWitness repository. The short entry in `AGENTS.md` points here so future repository sessions can reuse it without requiring the full policy in every prompt. Explicit task requirements and higher-priority instructions still apply.

Treat available model/token/credit usage as a scarce engineering resource. Complete the requested work to the required quality at the minimum practical usage cost. A usage window may be approximately five hours; use actual available quota information rather than assuming its duration or remaining capacity. A reset time is not a task deadline or permission to omit work.

## 1. Budget awareness

- If the environment exposes remaining quota, usage windows, reset times or consumption, inspect them when useful: once near the beginning, before a particularly expensive phase, or near the end when budget matters.
- Do not poll repeatedly or keep attempting to discover unavailable quota information.
- Do not purchase credits, redeem resets or change account settings without explicit authorization.

## 2. Model delegation

- When delegation and model selection are available, choose the least expensive model that can reliably perform the bounded subtask.
- Mechanical work can use lighter models: file/reference inventories, formatting, simple transformations, localization key comparisons, deterministic validation and straightforward tests from an established pattern.
- Reserve stronger models/reasoning for architecture, subtle debugging, diagnostic semantics, security, ambiguous failures, complex refactoring, UX decisions and difficult integration reviews.
- Delegate only when useful independent work can proceed and the result is easy to verify. Account for coordination and rework cost; do not fragment tasks automatically.
- Do not assign redundant investigations unless independent verification has a concrete benefit.
- If selection/delegation is unavailable, continue efficiently with the current model. Do not claim to have switched models when no switch occurred.

## 3. Reasoning effort

- Use low or medium reasoning for deterministic work, and medium for implementation with a clear specification when sufficient.
- Use high reasoning for genuinely complex architecture, debugging, security or diagnostic questions.
- Do not use maximum/xhigh reasoning for routine tasks. Escalate when a cheaper approach is insufficient; do not mechanically try a weak approach for a known high-risk problem.

## 4. Surgical reading

- Prefer targeted searches, relevant symbol ranges, diffs and status checks over repeated whole-file or whole-tree reads.
- Reuse established facts until something relevant changes.
- Inspect only the dependencies needed to understand the current subsystem.
- Read mandatory instructions completely; efficiency does not justify skipping required context.

## 5. Context reuse

- Do not repeatedly restate the specification or explain already-established architecture.
- Use repository documentation as persistent project memory.
- Keep progress updates short and useful: important findings, necessary decisions, user input or meaningful milestones. Honor any required communication cadence.

## 6. Tool efficiency

- Batch independent deterministic searches and checks when practical, keeping output bounded and understandable.
- Make coherent related edits before rebuilding; avoid edit/full-build loops for every small change.
- Run expensive complete validation at meaningful integration points and final verification, plus any explicitly requested baseline gate.

## 7. Test strategy

- Start with the narrowest relevant tests: History/ViewModel/UI for History work, Design for tokens, import/security for importers.
- Broaden validation after the affected subsystem is green.
- Run the complete suite at final verification for implementation passes and whenever the task requires it. Documentation-only changes should receive proportionate documentation checks unless broader tests are explicitly required.
- Never weaken tests, omit required validation or report an unexecuted test as passed to save usage.

## 8. Web research

- Avoid broad or repeated research unless the task needs it.
- Reuse existing project references and authoritative documentation.
- Search to resolve a concrete uncertainty or satisfy an explicit verification requirement; do not repeat searches already answered.
- Stop browsing for inspiration once the relevant design or technical decision is adequately specified.

## 9. Output discipline

- Do not paste complete files, long logs, full diffs, design documents or hundreds of passing test names into chat.
- Store requested detailed reports in files at the user's specified location, separate from repository documentation when requested.
- Keep final chat output to important completed work, actual verification, significant measurements, unresolved limitations and requested paths.
- Clearly distinguish measured facts, assumptions and unperformed validation.

## 10. Avoid unnecessary rework

- Inspect the current implementation and its abstractions before changing a mature subsystem.
- Preserve working code and unrelated user changes. Prefer minimal coherent changes over aesthetic rewrites.
- Introduce dependencies only when they materially improve the required result.

## 11. Project memory

- Use `AGENTS.md`, `docs/design/*`, architecture documentation, fixtures and existing validated implementation as established project references.
- Consume machine-readable contracts instead of reconstructing their meaning from screenshots or prose.
- Consult prior reports for recorded evidence, but do not treat claims as newly verified or silently perpetuate discrepancies.

## 12. Escalation

When a problem appears, start with the cheapest reliable diagnostic method, inspect targeted evidence, and run focused tests. Escalate to broader analysis, stronger reasoning or additional agents only when justified. Avoid speculative investigation without an evidence-driven question.

## 13. Stop conditions

Stop when the requested acceptance criteria are satisfied, required tests and validation are complete, and no significant known in-scope issue remains. Do not spend remaining budget inventing optional improvements or polishing indefinitely. If blocked, report exactly what is incomplete and what is needed; do not relabel incomplete work as finished.

## 14. Quality floor

Never save usage by skipping requested work, weakening tests, avoiding necessary investigation, leaving known in-scope bugs unfixed, reducing correctness/security/diagnostic reliability, replacing required implementation with TODOs, or claiming validation that did not occur.

Spend the necessary budget when quality genuinely requires additional reasoning, testing, research or implementation. The objective is minimum practical token/credit cost subject to full requested quality and completeness, not minimum work.
