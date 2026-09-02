# Domain Documentation Layout

This repository uses a **single-context** domain documentation layout.

## Locations

- **Canonical Domain Model**: `CONTEXT.md` at the repository root.
- **Architectural Decision Records (ADRs)**: `docs/adr/` containing numbered ADR files (e.g., `0001-*.md`).
- **Architecture Baseline Artifacts**: `docs/architecture/` containing the structure-baseline
  Registration Trace, Migration Manifest, module/domain ownership, Evidence Class, and
  Build Fingerprint artifacts established by ADR 0006. Changes to migration, ownership, or
  evidence contracts must update the corresponding artifact there.
- **Implementation and Runtime Audit Reports**: `audit/YYYY-MM-DD/` containing
  `Implementation-<ver>-<HHMM>.md`, `RuntimeFix-*`, and `RuntimeAcceptance-*` reports.

The repository root may also contain project-level documents such as `README`, `CHANGELOG`,
`LICENSE`, `CONTRIBUTORS`, checklists, and phase reports. These are project documents rather
than domain-model files and are not subject to the single-context placement rule; the domain
model itself remains defined by `CONTEXT.md`.

## Consumer Rules

1. Agents must read `CONTEXT.md` before designing or modifying multi-observer lifecycle, state replication, or ledger logic.
2. Changes to core terminology or architecture must update `CONTEXT.md` and record a new ADR in `docs/adr/`.
3. ADRs are immutable historical records of architectural choices. The `# 000X:` prefix on
   an ADR title line is a display convention and changing that prefix does not change the
   ADR's content or number.
