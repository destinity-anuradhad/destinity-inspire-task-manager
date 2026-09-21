# Phase 0 reconciliation evidence — 2026-08-31

Scope: configured local development database. The checks were read-only; no migration or repair statement was executed.

## Observed state

| Check | Result |
|---|---:|
| Tasks | 58 |
| Tasks with a non-empty legacy `Owner` | 1 |
| `TaskOwners` rows | 0 |
| Tasks with normalized assignments | 0 |
| Sprints | 6 |
| Active sprints | 1 |
| Duplicate application display names | 0 |
| Unmatched current legacy owner tokens | 0 |
| Current legacy/normalized differences | 1 |

The remaining current legacy owner resolves uniquely, but it represents only one task. It is safe for the guarded legacy backfill to copy that current value; it is not evidence that assignments for the other tasks were historically empty.

## Additional recovery evidence

The activity log contains three `Owner Changed` events affecting two tasks. One task still has its legacy owner value; the other does not. The latter is a recovery candidate, but it must not be restored automatically until its later activity and a database backup are compared and a product owner approves the mapping.

No other owner/assignment-labelled activity was found. Consequently, activity history alone cannot reconstruct assignments for the full task set.

## Migration and constraint discrepancy

`__EFMigrationsHistory` records `20260728000004_RenameLeadToTeamLead` as applied. The live check constraint nevertheless remains untrusted and permits `Lead` rather than `TeamLead`. This explains the audited HTTP 500 when application code writes `TeamLead`.

The new recognized `20260831000000_RepairTeamLeadConstraint` migration replaces that constraint and validates all current role values. It has been generated into an idempotent rehearsal script but has not been applied.

`20260831000100_BackfillVerifiedLegacyTaskOwners` copies only current, uniquely matched legacy owner values and aborts on ambiguous or unmatched names. Its rollback intentionally does not delete recovered assignments.

## Required decision before database deployment

1. Locate and restore the best pre-normalization backup to an isolated database.
2. Run the same reconciliation report against that backup.
3. Compare assignment rows, legacy owner values, and owner-change activity by task.
4. Produce a signed mapping of every assignment to recover, including its evidence source.
5. Add approved mappings through a separately reviewed, additive repair script.
6. Rehearse both new migrations and the approved repair script twice before production deployment.

Until those steps are complete, owner-dropping migrations and sprint-history mutations remain frozen.
