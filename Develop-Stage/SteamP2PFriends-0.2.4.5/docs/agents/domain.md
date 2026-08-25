# Domain Documentation Layout

This repository uses a **single-context** domain documentation layout.

## Locations

- **Canonical Domain Model**: `CONTEXT.md` at the repository root.
- **Architectural Decision Records (ADRs)**: `docs/adr/` containing numbered ADR files (e.g., `0001-*.md`).

## Consumer Rules

1. Agents must read `CONTEXT.md` before designing or modifying multi-observer lifecycle, state replication, or ledger logic.
2. Changes to core terminology or architecture must update `CONTEXT.md` and record a new ADR in `docs/adr/`.
3. ADRs are immutable historical records of architectural choices.
