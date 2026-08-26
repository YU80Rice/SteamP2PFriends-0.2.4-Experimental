# Local Markdown Issue Tracker

Issues and specs for this repository are tracked as local Markdown files under the `.scratch/` directory at the repository root.

## Directory Structure

```
.scratch/
├── <issue-slug>/
│   ├── issue.md
│   └── spec.md
```

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
`spec.md` layout above. This exception does not permit changing archived versions.
