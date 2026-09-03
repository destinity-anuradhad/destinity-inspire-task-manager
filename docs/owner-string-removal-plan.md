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
- Migrate each reader to resolve owners via the junction, with tests per reader proving parity:
  - [x] **Notifications** — `NotifyAssignedAsync`, `NotifyCommentAsync`, `NotifyBlockedAsync`, and the
    in-app owner-comment notification now read owners from `TaskOwners`. Proven by
    `OwnerNotificationStage1Tests` (incl. an owner present in the junction but absent from the string is
    still notified — the string no longer drives identity). *(done 2026-09-03)*
  - [ ] **Capacity / over-allocation** — group by owner user id instead of `Owner` name.
  - [ ] **Reporting / dashboards** — owner facets and per-owner rollups.
  - [ ] **CSV export / import** — emit and map owners by validated user identity.

**Stage 2 — derive the display string, stop hand-writing it.**
- Compute the `Owner` display string from `TaskOwners` (join to `AppUser.DisplayName`) at read time
  (or via a maintained projection), so writes no longer set it directly.
- Keep the column temporarily, now write-derived, to avoid breaking any missed reader.

**Stage 3 — drop the column.**
- Once no code reads `TaskItem.Owner` (verified by removing the property and compiling green + full
  test pass), add a migration dropping the column.
- Update CSV import/export to speak user ids / validated display names against `AppUser`.

## Verification gates (each stage)

- Full unit + integration suite green.
- A reconciliation query: for every task, the set of owner user-ids derived from the new path equals
  the set derived from the legacy string (zero-diff), run before each stage's cutover.
- Notification/capacity/report fixtures unchanged.

## Acceptance (backlog #7)

> All writes use user IDs; multi-owner behavior defined; legacy string read-only during migration then
> removed; reconciliation is zero-diff.

Stages 1–2 satisfy "writes use IDs / legacy string read-only"; Stage 3 satisfies "then removed".
