# Non-destructive existing-database upgrade

## Rule

The existing Task Tracker database is a system of record. Do not delete,
truncate, recreate, or drop its tables, columns, assignments, sprint history,
or migration history as part of this upgrade.

Do not run `dotnet ef database update` or the generated idempotent migration
script against an existing database while the current migration chain contains
the `DropTasksOwner` path.

## Required evidence

Work only against an isolated restore of a verified backup. First run:

1. `tools/phase0/reconcile-production-data.sql`
2. `tools/phase0/validate-non-destructive-upgrade.sql`

Save every result set with the backup identifier and UTC timestamp. The
production database remains read-only until the results are reviewed.

## Upgrade design

The replacement upgrade is a separately reviewed SQL script, not a change to
historical EF migrations. It must be idempotent and use this order:

1. Add missing tables, columns, indexes, and constraints only when absent.
2. Copy legacy assignment values into `TaskOwners` only where each display name
   maps to exactly one user.
3. Validate row counts, assignment differences, sprint membership, and every
   new foreign-key candidate.
4. Stop with an error on ambiguity, unmatched ownership, duplicate identities,
   or invalid historical references.
5. Leave legacy columns and tables intact until two successful rehearsals and
   written approval confirm that every record is reconciled.

The script may insert missing derived records and add nullable structures. It
must not issue `DROP`, `DELETE`, `TRUNCATE`, or destructive `ALTER COLUMN`
statements.

## Rehearsal acceptance

Run the reviewed script twice, each time from the same original restored
backup. It is acceptable only when both runs produce the same reconciliation
results, preserve all baseline record counts, and allow the application build,
automated tests, and role-based smoke tests to pass.

## Production release

After rehearsal approval, schedule a maintenance window, take and verify a new
backup, execute the approved additive script once, rerun reconciliation, and
retain the verified backup as the rollback point. Do not use an EF `Down`
migration as rollback for recovered assignments or historical sprint data.
