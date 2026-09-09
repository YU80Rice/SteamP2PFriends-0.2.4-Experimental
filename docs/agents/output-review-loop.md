# Output Review Loop

A hard rule for every artifact this workspace outputs: production source changes, tests, DLL artifacts, and audit reports. An artifact is a **formal output** only after this loop closes with both review axes **CLEAN**. Static verification (build, test runners, static gates) is the per-round baseline; it never replaces the review.

## Loop

1. **Red first** — write the failing test at an agreed seam, then the minimal implementation that turns it green.
   - Done when: the new test is observed red, then green, and the full suite passes.
   - When a pure host cannot construct a seam, record the seam gap in the ticket and the audit. Every gap is named; none is skipped silently.

2. **Dual-axis review** — dispatch the Standards and Spec reviewers as two independent subagents, each in its own context, each reviewing the current round's incremental diff per the `/code-review` flow.
   - Done when: both axes have returned their own reports. Two fresh contexts, one per axis, every round.
   - **Fresh-instance rule**: every round spawns two NEW subagent instances, dispatched with `subagent_type` explicitly set (`standards-reviewer` / `Spec-Reviewer`). Never resume a previous round's reviewer instance (no SendMessage continuation, no persistent-session reuse): a carried-over context anchors the reviewer on its own earlier analysis and **voids the round** — verdicts produced by continuation do not count toward the CLEAN chain.

3. **Re-review** — a finding means a fix, and the fixed increment returns to step 2.
   - Done when: Standards reports no hard violation and Spec reports no gap or deviation.
   - Judgment-call smells stay unblocking only when they are explicitly listed as deferrable in the audit; every deferral is named.

4. **Close the loop** — record the chain (rounds, findings, fixes, re-review verdicts) in the round's audit report, then grant the artifact identity (SHA-256, MVID, Case-ID).
   - Done when: the audit names every round and both final verdicts are CLEAN.
   - Intermediate DLLs produced before closure carry no Case-ID; identity is granted to reviewed artifacts only.
   - **本仓库注(2026-09-08)**:产物身份的可复现性要求见 `docs/agents/real-machine-test-loop.md` 与 audit/2026-09-08/IndependentReview-0.2.4.8-Ticket11-0751.md——构建指纹必须是可复现身份(不依赖构建绝对路径),否则不授予。

## Vocabulary

- **CLEAN** — an axis verdict with no blocking findings. Deferrable smells are listed, not counted.
- **Red / green** — the TDD gate at step 1: the test fails on the bug, then passes on the fix.
- **Dual-axis** — Standards (documented standards + smell baseline) and Spec (issue/spec fidelity), always two independent contexts.
