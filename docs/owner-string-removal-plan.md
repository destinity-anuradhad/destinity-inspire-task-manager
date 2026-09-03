# Plan: remove the legacy `TaskItem.Owner` string (backlog #7 completion)

**Status:** the authoritative model already exists — the `TaskOwners` junction (user-id based) is
populated and kept in sync by `SyncTaskOwnersAsync`, and legacy names were reconciled by
`BackfillVerifiedLegacyTaskOwners`. What remains is retiring the denormalized `Owner` **string**, which
is still read in many places (display, notifications, capacity, reporting, CSV export/import). Because
that read surface is wide, removal must be **staged and verified**, not done in one edit — hence it is
tracked here rather than force-completed.

## Why it's risky to do blindly

`Owner` is a comma-joined display-name string. Several behaviours still depend on it:

- Assignment notifications and watcher/mention resolution parse names from `Owner`.
- Capacity/over-allocation warnings group by `Owner`.
- Board/list/reporting/export surfaces display `Owner`.
- CSV import maps an owner column to `Owner`.

Names are not unique keys, so any path that resolves a name back to a user is a latent correctness bug
the junction table fixes — but only once every reader is migrated.

## Staged plan

**Stage 1 — make the junction the single read source (no schema change).**
- Add `GetOwnerUsersAsync(taskId)` sourced from `TaskOwners`, keeping `Owner` populated for display only.
Migrate the **identity** readers (which *user* owns / is-assigned-to a task) to the junction now — using
the authoritative id is strictly more correct. Each gets a parity test.
  - [x] **Notifications** — `NotifyAssignedAsync`, `NotifyCommentAsync`, `NotifyBlockedAsync`, and the
    in-app owner-comment notification read owners from `TaskOwners`. Proven by
    `OwnerNotificationStage1Tests` (an owner in the junction but absent from the string is still
    notified — the string no longer drives identity). *(done 2026-09-03)*
  - [x] **Personal dashboard "my tasks"** — the owned set is resolved from the junction, fixing the
    `Owner.Contains(displayName)` substring bug (e.g. "Sam" matching "Samantha"). Proven by
    `DashboardOwnerStage1Tests`. *(done 2026-09-03)*
**Stage 1 identity readers are complete** with the two above — the remaining `Owner` readers are all
name-keyed aggregations/config, which reading the code confirms are **Stage 2**, not Stage 1.

**Moved to Stage 2 after code review (2026-09-03):**
- **Capacity / over-allocation** — first triaged as a stage-1 identity reader, but the sprint capacity
  system (`SprintService.GetCapacityAsync`/`GetIndexDataAsync`, the over-capacity warning in
  `TaskService.Writes`) is **name-keyed throughout**: it groups `task.Owner` names, keys
  `SprintCapacity` by `OwnerName` (its `UserId` column exists but is unused by every reader), matches
  team members by `DisplayName`, and layers co-owner estimate-splitting + effective-capacity-from-leave.
  Migrating only the warning to ids would make it disagree with the view. **Prerequisite:**
  verify/backfill `SprintCapacity.UserId`, then migrate the view + warning + `SaveCapacityAsync`
  together, behind the reconciliation gate.
- **Timeline owner-facet** (`TaskService.Queries`), **team owner-stats + charts**
  (`DashboardService`/`ChartService`), **owner search/sort/filter**, **CSV export/import** — all
  list/group/search by owner **name**. Migrating now would silently **drop any not-yet-reconciled
  legacy owner name** from a filter/report — exactly what the reconciliation gate is there to catch.
  They move once the string is derived from the junction and reconciliation is zero-diff.

**Stage 2 — derive the display string, stop hand-writing it.**
- [x] **Write-side derivation done (2026-09-03).** `SyncTaskOwnersAsync` now sets `Owner` from the
  resolved owners (canonical display names, caller order, de-duplicated) on *every* write path
  (`SaveAsync`, `QuickSetOwner`, `ClaimTask`, `BulkChangeOwner`) — the junction is authoritative and the
  string is a lock-step cache, so the two can no longer drift (**reconciliation-clean by construction**
  for all new writes; only pre-change rows could be dirty). Proven by `OwnerStringDerivationStage2Tests`.
  *(Note: case normalization additionally happens on SQL Server's case-insensitive collation; the
  InMemory test provider is case-sensitive, so tests assert spacing/dedup/order, which hold on both.)*
- [ ] Migrate the read-side aggregations that need **identity** (capacity/over-allocation) to the
  junction; keep pure display readers on the (now-canonical) string until the column is dropped.
- Column stays, now fully write-derived, so no reader breaks.

**Stage 3 — drop the column.**
- Once no code reads `TaskItem.Owner` (verified by removing the property and compiling green + full
  test pass), add a migration dropping the column.
- Update CSV import/export to speak user ids / validated display names against `AppUser`.

## Verification gates (each stage)

- Full unit + integration suite green.
- **Reconciliation gate — built (2026-09-03).** `OwnerReconciliationService` compares, per task, the
  owner user-id set from `TaskOwners` against the ids the legacy string resolves to, and flags any
  disagreement or any name that resolves to zero/multiple users. A System Admin runs it at
  **`GET /Diagnostics/OwnerReconciliation`** (read-only); `isClean = true` (zero mismatches, zero
  unresolvable names) is the go/no-go for each Stage-2 cutover and the column drop. Covered by
  `OwnerReconciliationTests`.
- Notification/capacity/report fixtures unchanged.

## Acceptance (backlog #7)

> All writes use user IDs; multi-owner behavior defined; legacy string read-only during migration then
> removed; reconciliation is zero-diff.

Stages 1–2 satisfy "writes use IDs / legacy string read-only"; Stage 3 satisfies "then removed".
