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
