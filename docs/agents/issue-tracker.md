# Local Markdown Issue Tracker

Issues and specs for this repository are tracked as local Markdown files under the `.scratch/` directory at the repository root.

## Directory Structure

```
.scratch/
├── <issue-slug>/
│   ├── issue.md
│   └── spec.md
```

## Ticket Field Formats

The repository currently contains three documented metadata styles:

1. **Front-matter YAML** — used by
   `.scratch/minecraft-lan-experience-multi-domain-sync/issue.md` with fields such as
   `title`, `status`, `labels`, `created_at`, and `updated_at`. Its field vocabulary is
   owned by `triage` and `to-spec`.
2. **Key-value bullets** — used by
   `.scratch/structure-baseline-0-2-4-8/issue.md` with fields such as
   `Label`, `Type`, and `Status`. Its field vocabulary is owned by `to-spec` and
   `to-tickets`.
3. **Frozen numbered issues** — used by
   `.scratch/structure-baseline-0-2-4-8/issues/*.md` with `What to build`, `Blocked by`,
   `Status`, and checklist items. These are controlled graph inputs and are updated
   in place only while the frozen graph is active; the existing exception is intentional.

The style is determined from the file's location and fields. A tool must not rewrite one
style into another merely to normalize presentation.

## Wayfinder Map Namespace

`map-*/map.md` plus `ticket-*/ticket.md` (or `issue.md`) is the **wayfinder-only namespace**.
Its vocabulary includes `Label: wayfinder:map`, `wayfinder:grilling`, `wayfinder:task`,
and `wayfinder:done`, together with `Type: HITL|AFK`, `Parent:`, and
`Status: Open (Frontier)`. Wayfinder maps are excluded from the normal `triage` and
`to-tickets` scan scope. When a wayfinder map is closed and admitted to the main workflow,
it is rewritten through `/to-spec` into the standard `<issue-slug>/issue.md + spec.md`
layout.

## Root-Level `.scratch` Data Files

Process data and logs that are not tickets—such as symbol dumps and test logs—may live
directly under `.scratch/`. Their names must carry the owning ticket prefix, for example
`ticketNN-*.txt` or `ticketNN-*.log`. These files are evidence inputs or process artifacts,
not issue definitions, and must not be interpreted as tickets.

The global `*.log` ignore rule explicitly re-includes `!.scratch/ticket*-*.log` (see
`.gitignore`), so ticket-prefixed evidence logs under `.scratch/` are tracked and archived
normally; other logs remain ignored.

## Ticket Ownership and Lifecycle Fields

Field ownership is separated as follows:

- `Owner:` is a map-level responsibility field owned by wayfinder/map.
- `Parent:` belongs to a wayfinder map.
- `Blocked by:` records dependency edges owned by `to-tickets`.
- `Label:` belongs to the applicable vocabulary owner: wayfinder labels or the canonical
  triage labels.
- `Type:` (`HITL`, `AFK`, or `Specification`) belongs to wayfinder or `to-spec`.
- `Status:` records ticket lifecycle state, such as `implemented-pending-runtime`, and is
  owned by the implementation workflow.

The five triage labels (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`,
`wontfix`) express **who should handle an issue**. A ticket lifecycle status, such as
`implemented-pending-runtime` or `Open (Frontier)`, is a separate dimension; neither
dimension replaces the other.

`audit/YYYY-MM-DD/` is the repository location for implementation
(`Implementation-<ver>-<HHMM>.md`), runtime-fix (`RuntimeFix-*`), and runtime-acceptance
(`RuntimeAcceptance-*`) reports. New reports must use a ticket-bearing name:
`Implementation-<ver>-<ticket>[-<HHMM>].md`, `RuntimeFix-<ver>-<ticket>[-<HHMM>].md`, or
`RuntimeAcceptance-<ver>-<ticket>[-<HHMM>].md` — the ticket is required, the time (HHMM)
optional. Historic HHMM-only names are frozen; their ticket mapping is indexed in
`audit/README.md`.

## Conventions

- Every issue directory is named with a kebab-case slug representing the topic or ticket.
- `issue.md` contains the problem statement, context, and triage status.
- `spec.md` contains the detailed technical specification and acceptance criteria.
- Skills like `triage`, `to-tickets`, and `to-spec` read from and write to these files.

## Frozen dependency graph exception

The frozen `structure-baseline-0-2-4-8` dependency graph predates this single-context
layout and is intentionally preserved under `.scratch/structure-baseline-0-2-4-8/issues/`.
Its numbered issue files are controlled graph inputs and may be updated in place while
that graph is active; new independent issues must use the `<issue-slug>/issue.md` and
`spec.md` layout above. This exception does not permit changing archived versions. It is
retired when the `0.2.4.8` structure baseline is accepted; afterward
`structure-baseline-0-2-4-8/issues/` becomes a read-only archive and new issues must use
the standard layout.

`.github/ISSUE_TEMPLATE/` is a historical/legacy location reserved for a possible future
GitHub workflow. The local Markdown tracker is authoritative today, and the GitHub
templates are maintained manually; automated workflow changes must not treat them as the
active tracker.
