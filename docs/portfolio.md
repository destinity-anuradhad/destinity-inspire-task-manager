# Portfolio — cross-team dependencies

A portfolio-level view (Phase 4) that surfaces **blocking dependencies which cross team boundaries** —
where one team's work is held up by another team's — built from the existing task-link graph. Read-only.

Available under **Portfolio → Cross-team dependencies** (`/Portfolio/Dependencies`) to team
administrators and system administrators.

## What counts as a cross-team dependency

The task-link graph already models blocking relationships (`Blocks` / `Blocked By` and
`Predecessor` / `Successor`). This view reduces every such link to a **blocker → blocked** edge using
the shared `TaskLinkRules` semantics, then keeps only the edges where the two tasks belong to
**different teams**. Non-blocking links (`Related`, `Duplicate`) are ignored.

Each edge shows:

- the **blocked** task (the one waiting) and its team/status,
- the **blocker** task and its team/type/status,
- an **at-risk** flag — true when the blocker is **still open** (not in a done state for its item type),
  meaning the blocked team is genuinely waiting.

Edges are ordered at-risk first. Summary counts show the total cross-team dependencies and how many are
at risk.

## Scope

- **System administrators** see every cross-team dependency across the org.
- **Team administrators** see only the dependencies that touch their active team — either their team is
  blocked by another, or their team is blocking another.

## How "done" is determined

The at-risk flag uses `TaskStatusRules.IsDone(itemType, status)`, the same item-type-aware completion
rule the rest of the system uses (e.g. a Task is done at `Done`; a Bug/Story at `Resolved`/`Closed`).
So a dependency clears the moment the blocker reaches a terminal state for its type.

## Notes

- Soft-deleted tasks are still resolved (via `IgnoreQueryFilters`) so a dependency on a deleted blocker
  is surfaced rather than silently dropped.
- Reciprocal links (a `Blocks` plus its mirror `Blocked By`) are de-duplicated to a single edge.
- This reuses the same blocking semantics as dependency-cycle detection and completion gating, so the
  portfolio view and the task command can never disagree about what "blocks" means.
