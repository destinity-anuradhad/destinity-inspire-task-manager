# CSV Import

Bulk-create tasks from a CSV. Import is **two-phase and non-destructive**: uploading only *previews*
(parses + validates, writes nothing); a separate confirm step *applies* the valid rows through the
same governed task command the UI and API use. Jobs and per-row results are retained for audit.

Available under **Admin → Import** (`/Import`) to system and team administrators. Imported tasks are
created in the caller's active team.

## File format

First row is a header. Column order does not matter; matching is by name (case-insensitive).

| Column | Required | Notes |
|---|---|---|
| `Goal` | **yes** | Task title |
| `ItemType` | no | `Epic` / `Feature` / `Story` / `Task` / `Bug` (default `Task`) |
| `Status` | no | Any configured work state (default = first work state) |
| `Priority` | no | Free text |
| `Owner` | no | Display name |
| `TargetDate` | no | Parseable date, e.g. `2026-10-01` |
| `Weight` | no | Numeric estimate |
| `EstimateUnit` | no | Default `Points` |
| `Labels` | no | Free text |
| `ExternalKey` | no | Idempotency key (see below) |

```csv
Goal,ItemType,Status,Priority,TargetDate,Weight,ExternalKey
Build login page,Task,New,High,2026-10-01,5,IMP-001
Fix logout bug,Bug,To Do,Medium,,3,IMP-002
```

## Validation

Preview flags each row `Valid` or `Invalid` with a reason (missing goal, unknown item type/status,
unparseable date/weight). Only `Valid` rows are eligible to apply. On apply, each row is created
through the governed command, so its own rules still apply — a row that the command rejects is
recorded as `Failed` with the error, without aborting the rest of the import.

## Idempotency

A row with an `ExternalKey` that was already successfully imported for the team is **skipped** on
re-upload (recorded as `Skipped`). Rows without an `ExternalKey` are always created. This makes
re-running the same file safe.

## Limits

- Max 5 MB / 2000 rows per file.
- Applying is one-way; re-applying an already-applied job is a no-op.
