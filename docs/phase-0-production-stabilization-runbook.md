# Phase 0 production stabilization runbook

This runbook contains the required operational steps for the Phase 0 containment release. It does not authorize an in-place production migration. Perform the rehearsal against a restored backup and obtain approval for the reconciliation output before changing production data.

## Current containment defaults

- Runtime schema bootstrap and `Database.MigrateAsync()` are disabled by default and cannot be enabled outside Development.
- Legacy full-task edits are disabled. Creating a task and using the guarded single-status command remain available.
- Bulk task mutations and sprint assignment are disabled.
- Sprint create, edit, activation, completion, deletion, and capacity mutations are disabled.
- The owner/capacity migrations abort when data cannot be mapped without loss.

The temporary feature switches are:

```json
{
  "Features": {
    "AllowLegacyFullTaskEdit": false,
    "AllowLegacyBulkTaskMutations": false,
    "AllowLegacySprintMutations": false
  }
}
```

Do not enable these switches in production until their replacement command paths have regression coverage and the release owner has approved them.

## Backup and evidence

1. Put the application into a maintenance window and stop every application instance that can write to the database.
2. Record the database server, database name, deployed commit, application version, and UTC time.
3. Take a SQL Server full backup with checksum.
4. Run `RESTORE VERIFYONLY` for that backup.
5. Restore it under a new rehearsal database name on an isolated SQL Server instance.
6. Export `Tasks`, `TaskOwners`, `AppUsers`, `Sprints`, `SprintCapacities`, `ActivityLog`, and `__EFMigrationsHistory` to a protected evidence location.
7. Record row counts and hashes where the operating environment supports them.

The rollback point is the verified full backup. Do not rely on EF migration `Down` methods to recover lost display-name assignments or historical sprint membership.

## Read-only reconciliation

Run [reconcile-production-data.sql](../tools/phase0/reconcile-production-data.sql) against the restored backup. Save every result set.

Deployment must stop when any of these conditions is true:

- a legacy owner token matches zero or more than one user;
- display names are duplicated;
- legacy and normalized assignment sets differ without an approved explanation;
- a sprint-capacity owner cannot be mapped to exactly one user;
- a migration that drops `Tasks.Owner` or `TaskOwners` is pending;
- team-role values fall outside `TeamAdmin`, `TeamLead`, and `Member`;
- the restored row counts do not agree with the production snapshot.

Resolve ambiguous owners using authoritative evidence such as assignment activity, notifications, backups, or a signed product-owner decision. Never select a user merely because their name is similar. Record unresolved values as unresolved; do not delete them.

## Migration rehearsal

1. Confirm the application remains stopped for the rehearsal database.
2. Generate an idempotent SQL migration script from the exact release commit.
3. Review every `DROP`, `DELETE`, `ALTER COLUMN`, constraint, and data-copy statement.
4. Execute the script against the restored backup inside the approved change procedure.
5. Rerun the reconciliation report.
6. Verify task, assignment, sprint, capacity, and migration-history counts.
7. Run the automated test suite and the role-update smoke test against the rehearsed schema.
8. Restore the same original backup again and repeat the rehearsal to prove repeatability.

## TeamLead repair acceptance check

After the approved migration is applied:

- the `CK_TeamMembers_TeamRole` definition allows `TeamLead` and rejects `Lead`;
- all former `Lead` rows contain `TeamLead`;
- changing a member between `Member` and `TeamLead` succeeds;
- an unsupported role value is rejected by SQL Server;
- the team-management request returns a controlled success or validation response, never HTTP 500.

## Production deployment and rollback

Apply the reviewed script as a separate deployment step while application instances are stopped. Capture start/end times and all output. Start one application instance with all containment switches off, perform read-only smoke tests, then gradually restore traffic.

Rollback immediately if reconciliation counts change unexpectedly, migrations report partial failure, assignment rows disappear, role updates fail, or the application cannot read core task/sprint screens. Stop all instances, preserve logs and the failed database for investigation, restore the verified backup under the approved recovery procedure, and redeploy the previous application version.

## Phase 0 exit evidence

- Verified backup and successful restore rehearsal
- Signed assignment and sprint reconciliation results
- Reviewed migration SQL and repeatable rehearsal output
- Green solution build and test suite
- No vulnerable MailKit/MimeKit package reported by NuGet
- Successful `TeamLead` constraint smoke test
- Production configuration showing bootstrap and containment flags disabled
