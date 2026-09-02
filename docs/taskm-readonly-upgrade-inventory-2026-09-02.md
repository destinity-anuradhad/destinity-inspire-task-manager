# TaskM read-only upgrade inventory — 2026-09-02

Scope: local development database `TaskM`. All checks were read-only. No
database schema or data changes were performed.

## Legacy baseline

| Item | Result |
|---|---:|
| Tables | 9 |
| Recorded EF migrations | 0 |
| Users | 7 |
| Tasks | 21 |
| Tasks with non-empty legacy owner | 21 |
| `TaskOwners` table | Absent |
| Activity records | 5 |
| Duplicate user display names | 0 |
| Invalid target/start/end dates | 0 |

The schema is a pre-migration legacy baseline: `Tasks.Owner` is present and
`TaskOwners` is absent. Running the historical EF migration chain would be
unsafe because it includes destructive owner migration steps and assumes a
different migration history.

## Assignment evidence

Every distinct task owner resolves to exactly one existing user:

| Owner | Assigned tasks |
|---|---:|
| Anuradha | 3 |
| Chathuni | 3 |
| Kaviya | 3 |
| Piyumi | 3 |
| SachithK | 3 |
| SachithM | 3 |
| Stehani | 3 |

This supports an additive backfill into `TaskOwners`. The legacy `Tasks.Owner`
column must remain intact during that backfill and through validation.

## Next implementation step

Create a dedicated, idempotent upgrade script for this legacy baseline. It
must create missing structures and copy validated assignments without dropping
or modifying any existing data-bearing table or column. It must be rehearsed
against a database restore before use outside local development.
