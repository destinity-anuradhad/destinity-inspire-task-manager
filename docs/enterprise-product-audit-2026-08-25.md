# Task Tracker Enterprise Product Audit

**Audit date:** 25 August 2026  
**Repository:** `task-tracker`  
**Scope:** active application under `src/`  
**Assessment type:** source, schema, data, build, automated-test, dependency, authenticated multi-session, and browser audit

## Executive conclusion

**Production readiness: NOT READY — CRITICAL RISK**  
**Enterprise maturity: EARLY PRODUCT (1.5/5 overall)**

The application has a credible small-team work-tracking foundation: a polished task workspace, team scoping on most primary queries, five work-item types, multiple views, comments, attachments, watchers, saved filters, SignalR updates, sprint screens, dashboards, and role-aware UI. The production projects compile cleanly on .NET 10.

It is not yet safe to treat as an enterprise system of record. The decisive blockers are:

1. Status and completion invariants are enforced only on one update path and can be bypassed by full-edit and bulk operations.
2. Sprint completion moves incomplete tasks by overwriting their current `SprintId`; there is no immutable sprint scope or completion snapshot, so historic velocity and burndown can change later.
3. The owner-normalization migration sequence appears to have dropped prior assignment data. The current database contains 58 tasks, one legacy owner string, and no `TaskOwners` rows.
4. Authentication does not enforce account status, and team/role claims are not revalidated during a sliding eight-hour session. Removed or downgraded users can retain access.
5. Cross-team task-link and descendant-stat paths do not consistently enforce tenant/team boundaries.
6. The test project does not compile and is not part of the solution build, so the clean solution build is not a regression signal.
7. The email queue is memory-only, silently drops old messages under pressure, and has no retries, durable outbox, idempotency, or operational controls.
8. A database check constraint accepts `Lead`, while the application writes `TeamLead`; new lead membership writes can fail.

The recommended strategy is to harden integrity and authorization first, establish a single work-item command model and immutable sprint ledger second, then add enterprise management capabilities. Adding more screens before those foundations would increase migration and reconciliation cost.

## Scope, method, and limitations

### Evidence reviewed

- All active projects, controllers, services, models, migrations, Razor views, JavaScript, CSS sources, tests, manuals, and configuration under `src/` and `docs/`.
- EF Core model and migration history, plus read-only inspection of the configured local development SQL Server database.
- Production build, test compilation, pending-model-change check, NuGet vulnerability scan, and npm production dependency audit.
- A running local instance at `https://localhost:7276` and an unauthenticated Playwright rendering of the login page.
- Official public documentation for Jira, Azure Boards, and ClickUp to calibrate enterprise feature expectations.

### Important limitation

Authenticated journeys were executed with the supplied System Administrator and one disposable Member account created in the local Mobile team. That account was temporarily promoted/downgraded to test claim revocation, then fully removed with its tasks, sprint, attachment, notification, email log, membership, and activity artifacts. A working Team Lead session could not be created because the `TeamLead` database update itself fails with HTTP 500. Successful external email delivery was not attempted: the test used an `example.invalid` recipient and placeholder SMTP credentials, proving queue/attempt/failure logging without sending real mail. No production code was modified.

### Rating definitions

- **MISSING** — no usable implementation was found.
- **UI ONLY** — presentation exists without the required backend behavior.
- **BACKEND ONLY** — service/schema behavior exists but users cannot complete the journey.
- **PARTIAL** — meaningful behavior exists, but important rules, paths, or states are absent.
- **DEFECTIVE** — implemented, but an observed design or code path breaks its core invariant.
- **FULLY IMPLEMENTED** — coherent UI, service, persistence, authorization, and operational behavior were found for the stated scope.
- **NOT APPLICABLE / NOT REQUIRED** — intentionally outside the recommended product boundary.
- **NOT TESTED** — implementation may exist, but required runtime evidence was unavailable.

## System map

| Layer | Current responsibility | Assessment |
|---|---|---|
| `TaskTracker.Web` | MVC controllers, Razor UI, client interactions, cookie authentication, startup/bootstrap | Clear entry point, but the main Tasks page is a 4,748-line monolith with duplicated extracted partials and JavaScript. |
| `TaskTracker.Services` | Task/sprint/team/user rules, queries, notifications, email, SignalR | Better separated than the UI, but business rules vary by endpoint and lack a shared command/invariant layer. |
| `TaskTracker.Data` | EF Core context, models, migrations | Broad domain coverage, but several logical relationships lack foreign keys and migration history contains destructive/inconsistent normalization. |
| `TaskTracker.Tests` | xUnit service tests and in-memory helpers | Excluded from the solution and currently fails to compile. |

### Principal entities and relationships

```text
Team
├── TeamMember ── AppUser
├── Sprint
│   ├── SprintCapacity   (logical User relation; incomplete FK coverage)
│   └── SprintHoursLog   (logical Task relation; mutable aggregate)
├── TaskItem
│   ├── Parent TaskItem
│   ├── TaskOwner ── AppUser (schema exists; application still uses Owner text)
│   ├── TaskAttachment
│   ├── TaskWatcher ── AppUser
│   └── TaskLink ── TaskItem
├── ActivityLog
└── SavedQuery

AppUser
├── Notification
└── EmailLog
```

Several edges above are application conventions rather than enforced database relationships. That distinction is material for deletion safety, orphan prevention, and auditability.

## Core process maps

### Task creation and full edit

```text
Task modal/form
  → TasksController.Save
  → TaskService.SaveAsync
      → team lookup and coarse CanModify check
      → direct field copy, including Status/Sprint/Owner
      → optional open-child close guard
      → save current Task row
      → cascade parent status
      → assignment/watcher notifications
      → ActivityLog + team SignalR event
```

The full-edit path does not apply `TaskStatusRules.IsTransitionAllowed`, does not require actual hours for completion, does not bind an original row version, and does not validate work-item hierarchy types.

### Quick status change

```text
Board/list status action
  → TasksController.UpdateStatus
  → TaskService.UpdateStatusAsync
      → team + CanModify
      → transition rule (members only)
      → owner requirement
      → actual-hours requirement
      → open-child guard
      → save + cascade + notifications + history + SignalR
```

This is the strongest lifecycle path, but the same transition can be performed through weaker full-edit and bulk paths.

### Bulk status change

```text
Selected work items
  → BulkChangeStatusAsync
      → team scope + CanModify
      → owner check for active/done states
      → direct Status assignment
      → save + parent cascade
```

This path omits transition validation, actual-hours enforcement, open-child enforcement, per-item status history, watcher notification, concurrency checking, and consistent SignalR publication.

### Sprint completion

```text
Complete sprint
  → count current tasks
  → for each incomplete task: replace SprintId with target sprint/null
  → set sprint IsActive = false
  → reports later recompute from current Task.SprintId and ActivityLog text
```

There is no completion timestamp, committed-scope snapshot, carry-forward event, or immutable membership history. Therefore the application cannot answer reliably what was committed, completed, added, removed, or carried forward in a prior sprint.

### Email delivery

```text
Domain action
  → enqueue in bounded in-memory Channel (500, DropOldest)
  → single background reader
  → SMTP send
  → EmailLog success/failure
```

There is no durable outbox, retry schedule, deduplication key, dead-letter queue, delivery dashboard, or recovery after process restart.

## Detailed feature completeness matrix

| Capability | Status | Evidence and operational assessment |
|---|---|---|
| Create/edit/delete/restore tasks | PARTIAL | CRUD and soft-delete exist. Lifecycle, hierarchy, normalized owner, and concurrency invariants vary by endpoint. Permanent deletion is global-admin only. |
| Task table/list | FULLY IMPLEMENTED | Server-side pagination, filtering, sorting, and export are present for the primary table path. Authenticated browser behavior remains unverified. |
| Kanban board | PARTIAL | Drag/status UI and WIP indicators exist. WIP limits are not enforced and bulk/full-edit paths bypass board rules. |
| Calendar, timeline, backlog, team views | PARTIAL | Rich view set exists; several paths load all team tasks and the main Razor page duplicates implementations. Large-team performance is unproven. |
| Work-item types | PARTIAL | Epic, Feature, Story, Task, and Bug exist. Their fields and transitions are hardcoded and conflict with configurable work states. |
| Hierarchy and subtasks | DEFECTIVE | UI suggests Epic→Feature→Story→Task/Bug; backend validates only cycles. Arbitrary type relationships can be posted. No general nested subtask model or hierarchy policy. |
| Parent auto-cascade | DEFECTIVE | All-child completion can close certain parent types, but reopening a child leaves a closed parent closed. Task parents never auto-complete. |
| Weighted rollups | MISSING | Progress is leaf item-count based, not weight/estimate based. Traversal depth is capped. |
| Status lifecycle | DEFECTIVE | Quick status uses rules; save and bulk bypass them. Configured states such as `Not Started` and `On hold` have no member transitions. |
| Completion timestamps | DEFECTIVE | Normal status completion does not stamp `ActualEndDate`; profile completion metrics depend on that field. |
| Optimistic concurrency | DEFECTIVE | `RowVersion` exists, but original values are not round-tripped, so ordinary UI edits do not detect stale forms. |
| Assignees/owners | DEFECTIVE | A junction table exists, but production code predominantly reads/writes a comma-separated display-name string. The current data indicates assignment loss. |
| Reporter and creator | PARTIAL | Both concepts exist, but permissions conflate ownership, reporting, creation, and team leadership. |
| Labels | PARTIAL | Stored/searchable text; no governed label entity, color, permissions, usage analytics, or rename lifecycle. |
| Acceptance criteria | PARTIAL | Stored on work items. No structured acceptance/approval workflow. |
| Comments | PARTIAL | Create, mention, notify, and watch behavior exists. No edit/delete/reply/thread/moderation lifecycle. |
| Mentions | DEFECTIVE | Display-name matching is global, not restricted to active members of the task's team. Cross-team recipients can receive unusable or unauthorized deep links. |
| Watchers/followers | PARTIAL | Watch/unwatch, comments, and status notifications exist. Preferences and deactivated-user handling are incomplete. |
| Attachments | PARTIAL | Team-scoped list/upload/download checks exist. Content signatures are not verified; files are served from web root; relational cleanup relies on application code. |
| Task relationships/dependencies | DEFECTIVE | Links exist, but target team is not validated, types are not governed, dependency cycles are permitted, and blocking does not prevent completion. |
| Saved queries | PARTIAL | Basic saved filters exist. No expressive query language, sharing/governance, or rich sprint/hierarchy/custom-field filters. |
| Bulk operations | DEFECTIVE | Useful UI exists, but bulk operations bypass critical rules, audit, notification, and concurrency behavior. |
| Sprint create/edit/activate/complete | DEFECTIVE | Basic management works, but role behavior conflicts with manuals and historical states remain editable/reactivatable. |
| Sprint carry-forward | DEFECTIVE | Incomplete work can move, but source history is erased rather than recorded. Same-sprint and target-state safeguards are weak. |
| Sprint history | MISSING | No immutable sprint membership, committed scope, completion snapshot, scope-change log, or carry-forward lineage. |
| Burndown | DEFECTIVE | Reconstructed from current membership and activity-log text; full-edit/bulk gaps, moves, and reopenings distort results. |
| Velocity | DEFECTIVE | Recomputed from mutable current assignments and mixed estimate semantics. Historic values are not stable. |
| Capacity planning | DEFECTIVE | Per-sprint hours exist, but workload sums `Weight`, multi-owner work is double-counted, and daily capacity/days off are not applied. |
| Time tracking | DEFECTIVE | One mutable total per task/sprint replaces prior hours. No append-only entries, date, note, timer, remaining-work, or approval. |
| WIP control | UI ONLY | Limits are visual indicators, not policy enforcement or a governed override. |
| Dashboard/charts | PARTIAL | Broad presentation exists. Several metrics inherit status, ownership, and sprint-history defects. |
| User administration | PARTIAL | Admin listing/create/edit/approve/reject behaviors exist. Account status is not enforced at login; newly created users are not forced to change passwords. |
| Self-registration | UI ONLY | `Register.cshtml` posts to `/Account/Register`, but no corresponding controller action exists. An unauthenticated request redirects to login. |
| Teams and membership | PARTIAL | Multi-team membership and switching exist. Team-role claims can remain valid after removal/downgrade. |
| System roles | PARTIAL | `Admin` and `Member` exist; authorization is coarse and lacks permission policies/scopes. |
| Team roles | DEFECTIVE | `TeamAdmin`, `TeamLead`, and `Member` are used by code. The newest DB constraint permits `Lead`, not `TeamLead`. |
| Activity/audit log | PARTIAL | Useful events exist, but full coverage, immutable history, before/after values, bulk detail, and non-admin access are inconsistent. |
| In-app notifications | PARTIAL | Assignment/comment/mention/blocker/watched-status events and badge updates exist. Coverage, preferences, retention, and integrity are incomplete. |
| Email automation | DEFECTIVE | Core event emails exist, but delivery is lossy and non-durable. Email logs in the inspected database contain no delivery evidence. |
| Authentication | DEFECTIVE | Secure cookie attributes and local return URL checks are good. No account-status enforcement, lockout/rate limiting, claim revalidation, or session revocation. |
| Authorization/team isolation | DEFECTIVE | Most primary queries scope by team, but task links, descendant statistics, stale claims, mentions, and team-view user loading expose boundary weaknesses. |
| CSRF defense | PARTIAL | Most mutations use antiforgery. `Master/ReorderWorkStates` explicitly ignores it. |
| Security headers | PARTIAL | `X-Frame-Options` is present. No consistent CSP, HSTS evidence in the observed response, permissions policy, or broader header policy was found. |
| API/integration surface | MISSING | No supported public REST API, webhook/event contract, import, SCM integration, or automation integration framework. |
| External master data | BACKEND ONLY / DEFECTIVE | External project/client lookup code exists, but local FK tables are empty in the inspected database, so selected external IDs may violate new local FKs. |
| Recurring work | MISSING | No recurrence definition or scheduler. |
| Templates | MISSING | No task/project/sprint templates. |
| Checklists | MISSING | No checklist entity, item lifecycle, or progress rollup. |
| Releases/versions | MISSING | No release entity, version field, release notes, or release reporting. |
| Custom fields | MISSING | No governed extension schema or typed custom values. |
| Workflow automation | MISSING | Only hardcoded parent cascade and direct event notifications; no rule engine, conditions, actions, retries, or audit trail. |
| Project portfolio management | MISSING | Projects are external lookup labels rather than first-class governed containers with owners, dates, state, permissions, health, or budgets. |
| Billing/financial PPM | NOT APPLICABLE / NOT REQUIRED | Not recommended unless product strategy explicitly expands beyond work management. |
| Rich docs/whiteboards/AI | NOT APPLICABLE / NOT REQUIRED | Competitive differentiators, not prerequisites for a safe enterprise work tracker. |

## Authenticated runtime verification

### Primary-screen smoke test

With the supplied System Administrator in the Mobile team, all of these routes rendered HTTP 200 without a visible server-error page: My Dashboard, Team Dashboard, Tasks table, Kanban, timeline/calendar, team timeline, backlog, sprint board, Sprints, Teams, Team Settings, Master Files, Charts, Activity, Queries, Recycle Bin, and Profile.

This is a rendering/smoke result, not a correctness pass for the underlying metrics. The source and transaction findings below still apply.

### Member task lifecycle

The disposable Member completed this browser-driven sequence:

1. Logged in and auto-selected its single team.
2. Created a Task in `To Do`, automatically owned by the member.
3. Watched it, commented, uploaded/download-listed a text attachment, and retrieved history.
4. Moved `To Do → In Progress` through the status command; the transition succeeded.
5. Attempted `In Progress → Done`; the status command correctly rejected it and identified `Development Done`, `Blocked`, and `To Do` as valid next states.
6. Opened full edit and set the same task directly to `Done` with no actual hours; save succeeded. The resulting row had `Status=Done`, `ActualHours=null`, `ActualStartDate=null`, and `ActualEndDate=null`.
7. Soft-deleted the task; team-scoped retrieval then returned 404.

This runtime result upgrades the workflow-bypass concern from static risk to reproduced defect.

### Bulk permission and lifecycle probes

Three disposable Member-created tasks were bulk-set to `Done` with no actual hours. All three succeeded. The same Member then bulk-reassigned all three from itself to another team member; all three succeeded with zero denied items. This confirms both the lifecycle bypass and the unused assignment-policy issue.

### Cross-team isolation probes

From Mobile, direct retrieval and search for PMS Core task 57 correctly returned 404/empty results. However:

- `GetChildStats?id=57` returned HTTP 200 rather than a team-scoped not-found response.
- Adding a `Related` link from a Mobile task to PMS Core task 57 returned HTTP 200.
- Reading the Mobile task's links exposed the PMS Core task title, status, type, and ID.

The audit removed the cross-team relationship immediately afterward.

### Account, role, and membership revocation

- Temporarily setting the disposable account's database status to `Rejected` did not prevent login; it reached the Mobile dashboard. The status was restored after the probe.
- A fresh Member was redirected away from Master, Activity, and Sprints. These controls are enforced, though the access-denied handler returns a dashboard HTTP 200 rather than a 403 response.
- After promotion to Team Admin, the account could open Team Settings and Sprints.
- After downgrade to Member, its existing cookie was denied by Team Settings because that service revalidates membership, but the same stale session still opened Sprints with management UI. A fresh session was denied.
- After complete membership removal, the existing session still opened the Mobile Tasks workspace. A fresh login correctly landed on `No Team Assigned`.
- Updating the team role to `TeamLead` returned HTTP 500 with a SQL check-constraint conflict. The account was restored to Member after the probe.

### Sprint transaction probes

A disposable sprint and one incomplete task reproduced all of the historical-integrity concerns:

- Completing the sprint while selecting the same sprint as the carry-forward target returned success and reported one moved item; the task remained assigned to the now-inactive sprint.
- The inactive/completed sprint accepted edits to name, dates, and goal.
- Completing the same sprint a second time also returned success.
- Moving to backlog set the task's current `SprintId` to null.
- The sprint could then be deleted, leaving no sprint record from which to reconstruct the original commitment.

The sprint and task were permanently removed after verification.

### Notification and email probes

- Creating a task assigned to the disposable Member produced an in-app notification and the notification list contained the task title.
- Assigning through `QuickSetOwner` produced no assignment notification, showing that notification behavior is also path-dependent.
- The safe `example.invalid` assignment generated an `EmailLog` attempt. SMTP failed as expected because the running audit instance used placeholder credentials; the failure was recorded with `IsSuccess=false`.
- Successful provider delivery, retries, recovery after restart, and high-volume queue behavior remain unverified.

### Browser responsiveness and accessibility observations

- Login rendered without horizontal overflow at 390×844.
- Authenticated mobile Kanban measured a 1,018 px document width in a 390 px viewport. The desktop navigation, multi-column board, and 390 px-wide task canvas produce material horizontal overflow.
- Login labels are visually present but not programmatically associated with their inputs; Playwright label locators could not resolve them.

### Additional security/route probes

- Authenticated `/Account/Register` returned HTTP 404, confirming the registration view has no usable action.
- Posting the unchanged WorkState order to `/Master/ReorderWorkStates` without an antiforgery token returned HTTP 200, confirming the CSRF gap without altering the order.
- External module and client lookup endpoints returned HTTP 200 with zero rows in 36 ms for the inspected environment.

## Critical findings

### C-01 — Assignment normalization can lose production data

**Evidence**

- Migration `20260728000003_DropTasksOwner` drops the legacy owner field.
- Migration `20260812000000_RemoveTaskOwners` recreates an empty owner field and drops the junction table.
- Migration `20260817032043_NormalizationPhase1_TaskOwners` recreates the junction and backfills from the now-recreated string.
- Production services still read and write `TaskItem.Owner` instead of treating `TaskOwners` as authoritative.
- Inspected local data: 58 tasks, one non-empty owner string, zero `TaskOwners` rows.

**Impact:** prior assignments can be unrecoverable; permissions, dashboards, capacity, notifications, and accountability become incorrect.

**Required remediation:** freeze assignment migrations; restore from a verified backup/audit source; implement a rehearsed expand–migrate–verify–contract migration; make user IDs authoritative; retain display-name projection only for compatibility; add reconciliation reports and blocking data-quality gates.

### C-02 — Workflow invariants are path-dependent

`UpdateStatusAsync` checks transitions, owner, actual hours, and open children. `SaveAsync` directly copies status and only checks owner/open children. `BulkChangeStatusAsync` directly assigns status after an owner check. A member who can modify a work item can therefore bypass the strongest lifecycle rules.

**Impact:** invalid transitions, false completion, missing effort, open-child closure, missing notifications, and inconsistent audit history.

**Required remediation:** create one transactional status command used by every UI/API/bulk path. Validate type-specific workflow, permission, required fields, descendants, dependencies, concurrency token, and transition side effects in one place.

### C-03 — Sprint history is mutable and cannot support trustworthy analytics

`CompleteAsync` moves incomplete items by overwriting `Task.SprintId`, then sets `IsActive=false`. Reports later query the current `SprintId`. No sprint assignment event or completion snapshot exists.

**Impact:** prior commitment, scope change, spillover, completion rate, velocity, burndown, ownership, and actual effort can change after sprint close.

**Required remediation:** introduce sprint state, immutable sprint-work-item membership/assignment events, committed and completed snapshots, explicit carry-forward lineage, completion timestamp, and report from the ledger/snapshots.

### C-04 — Authorization can survive account or membership revocation

Login verifies password but not `AppUser.Status`. Team and role claims are stored in a sliding cookie and are not periodically validated against current membership. A removed or downgraded user may continue using prior privileges.

**Impact:** unauthorized access after deactivation, membership removal, or role downgrade.

**Required remediation:** reject non-active accounts, add security/session versioning, validate principals periodically, revoke sessions on security changes, derive authorization from current membership for sensitive commands, and add policy-based authorization tests.

### C-05 — Cross-team relationship paths break the isolation model

`AddLinkAsync` validates only the source task's team and modification right. The target task can belong to another team. Descendant-stat traversal also lacks consistent root and descendant team filters.

**Impact:** cross-team existence/metadata disclosure, unauthorized relationship mutation, and notifications to users outside the authorized team.

**Required remediation:** require both ends and every traversed node to share the active team; add composite integrity constraints where possible; return nondisclosing not-found responses; add adversarial team-isolation tests.

### C-06 — Automated regression tests provide no protection

The production solution builds, but the test project is excluded from `TaskTracker.slnx`. After a forced restore, test compilation fails because helpers call obsolete constructors and pass `NullEmailService` where `IEmailQueue` is required. No tests execute.

**Impact:** lifecycle, permissions, migrations, email, and reporting regressions can ship while CI appears green.

**Required remediation:** add the test project to the solution/CI, fix fixtures, require test execution, add migration tests against SQL Server, and gate merges on security/invariant test suites.

### C-07 — Email delivery is intentionally lossy

The bounded channel uses `DropOldest`; enqueue ignores the return value; memory is lost on restart; the worker does not retry; and SMTP failure has no dead-letter or operator action.

**Impact:** assignment and workflow notifications can disappear without user-visible failure, violating accountability expectations.

**Required remediation:** transactional outbox, durable dispatcher, retry/backoff, idempotency key, delivery state machine, dead-letter workflow, metrics/alerts, and administrative replay.

### C-08 — Database role constraint conflicts with application values

Application code and existing data use `TeamLead`; the newest check constraint permits `Lead`. It was added with `NOCHECK`, so old rows remain while new writes can fail.

**Impact:** adding or editing team leads can fail in production and environments can diverge.

**Required remediation:** correct the constraint in a forward migration, validate all existing data, remove stale comments/value aliases, and add an integration test that creates every supported role.

## High-risk findings

- **H-01:** `CanAssign` exists but is unused. Quick and bulk owner operations rely on `CanModify`, allowing members to reassign owned/created work through alternate endpoints.
- **H-02:** configurable WorkStates do not drive `TaskStatusRules`. Current data includes statuses with no allowed member transitions.
- **H-03:** reopening a child does not reopen or warn on a completed parent.
- **H-04:** hierarchy type constraints are client-side only; the backend accepts arbitrary parent types.
- **H-05:** dependencies neither detect cycles nor block completion.
- **H-06:** activity access is system-admin only although the service and manuals imply team-scoped access.
- **H-07:** Sprint role behavior conflicts with manuals: members cannot view, while team leads can manage almost all sprint operations.
- **H-08:** Team leads are offered member-creation capabilities but cannot open the management view used to perform them.
- **H-09:** created users are not forced to change passwords; password length requirements are inconsistent.
- **H-10:** sprint capacity compares task `Weight` to capacity hours, double-counts multi-owner items, and ignores days off/working-day data.
- **H-11:** sprint hours are a replaceable aggregate, not an append-only time ledger; current owner changes rewrite historical attribution.
- **H-12:** inactive sprints are editable/reactivatable and are not reliably distinguishable from completed sprints.
- **H-13:** current database has 43 tasks in inactive sprints, 26 incomplete, demonstrating ambiguous historical semantics.
- **H-14:** `local_setup.sql` is materially behind the current model and migration chain, including insecure sample material and incompatible schema assumptions.
- **H-15:** raw startup DDL and EF migrations both mutate schema; errors are caught and startup continues, risking partially upgraded databases.
- **H-16:** MailKit 4.8.0 and MimeKit 4.8.0 are reported with moderate-severity advisories by the current NuGet audit.
- **H-17:** uploaded file extensions and sizes are checked, but content signatures are not; files are placed below web root.
- **H-18:** `ReorderWorkStates` ignores antiforgery validation.
- **H-19:** assignment notifications are path-dependent: create/full-save can notify, while `QuickSetOwner` and bulk owner changes do not.
- **H-20:** authenticated mobile task views are not responsive; Kanban measured 1,018 px document width in a 390 px viewport.

## Data integrity and migration assessment

### Observed local database snapshot

| Measure | Value |
|---|---:|
| Tasks | 58 |
| App users | 7 |
| Teams | 2 |
| Sprints | 6 |
| Active sprints | 1 |
| Tasks assigned to inactive sprints | 43 |
| Incomplete tasks in inactive sprints | 26 |
| TaskOwners rows | 0 |
| Tasks with non-empty legacy Owner | 1 |
| Activity rows | 86 |
| Notifications | 5 |
| EmailLog rows | 0 |
| Local Modules / Clients | 0 / 0 |

This is not evidence of every deployment, but it is strong evidence that migration, owner, sprint-state, and local-lookup behavior need production data reconciliation before release.

### Missing or weak relational controls

Foreign-key enforcement is incomplete for team membership, sprint capacity/user, sprint hour/task, attachment, watcher, link, and notification relationships. Application checks cannot fully substitute for database integrity under concurrency, deployment errors, manual support operations, or future code paths.

### Migration release requirements

1. Back up and checksum production data.
2. Run read-only assignment, sprint-history, role-value, and orphan reconciliation reports.
3. Repair owner history from the best authoritative source before contracting legacy columns.
4. Correct constraints and add missing indexes/FKs using online-safe, staged migrations.
5. Rehearse upgrade and rollback on a production-sized copy.
6. Verify counts, sums, orphan checks, and permission samples before and after migration.
7. Disable raw schema bootstrap in production once migrations are authoritative.

## Security and access-control assessment

### Strengths

- Global authenticated fallback policy.
- Cookie `Secure`, `HttpOnly`, and `SameSite=Lax` settings.
- Local URL validation on login redirection.
- Antiforgery on most form and mutation endpoints.
- Password hashing uses ASP.NET Core's password hasher.
- Most primary task/sprint queries accept and apply active-team scope.
- SignalR updates use team/user/task groups rather than global broadcast.

### Gaps

- No active-status enforcement, lockout, request throttling, MFA/SSO, session inventory, or revocation.
- Coarse system/team roles instead of explicit permission policies.
- Stale role/team claims on long-lived sliding sessions.
- No consistent ownership policy reused across all commands.
- Cross-team relationship and statistics IDOR risk.
- Global mention directory exposure and notification of out-of-team users.
- No clear CSP and security-header baseline.
- No content inspection/quarantine for uploaded files.
- Configuration startup requires sensitive settings even when optional features are disabled; secrets must remain outside committed configuration.

## Performance, scalability, reliability, and operability

### Performance

- Primary table pagination is a good foundation.
- Kanban, calendar, timeline, and some dashboard paths load entire team datasets; this will not remain responsive at enterprise volume.
- Several reports aggregate after materializing rows in memory.
- Multi-owner comma-string expansion prevents efficient indexed queries.
- The 293 KB Tasks Razor view and duplicated JavaScript increase payload, maintenance, and regression risk.

### Reliability

- Email is not durable.
- Schema bootstrap can continue after exceptions.
- No evidence of health checks, readiness checks, circuit breakers, or transactional outbox processing.
- Sprint and task commands lack idempotency and a consistent transaction boundary for state, audit, notification, and SignalR effects.

### Observability

- Activity and email logs are useful starts.
- Missing: correlation IDs, command/event IDs, structured audit before/after values, queue depth, retry/dead-letter metrics, health endpoints, SLA dashboards, and alert rules.

### Deployment and maintenance

- Production projects target current .NET and build cleanly.
- Tests are outside the solution and broken.
- EF reports no pending model changes, but warns that `DailyCapacityHours` lacks an explicit database precision/type and can be silently truncated.
- EF tool/runtime patch versions differ.
- Production NuGet audit reports moderate MailKit and MimeKit advisories; npm production audit reports zero vulnerabilities.

## UX, responsive behavior, and accessibility

The login page rendered cleanly in Playwright at desktop and 390×844 mobile sizes. Desktop has a coherent two-panel hierarchy; mobile collapses to a readable single-column form with appropriately sized controls and no observed horizontal overflow. Primary authenticated System Admin screens also rendered, but authenticated mobile Kanban overflowed to 1,018 px in a 390 px viewport.

Static concerns:

- The main task UI is highly capable but dense and concentrated in a single Razor file.
- Several extracted task partials and `wwwroot/js/tasks.js` are not the active implementation, increasing drift risk.
- Modal focus management, focus trapping, escape behavior, error announcements, and keyboard drag/drop require runtime verification.
- Some interactions are pointer/drag oriented and need keyboard equivalents.
- CDN-loaded assets create availability, CSP, and offline/deployment variation.
- Registration presentation exists without a working registration journey.
- Manuals and actual role-visible actions conflict, which is an operational UX defect even when code permissions are technically enforced.

## Maturity scorecard

Scale: 0 absent, 1 early, 2 developing, 3 usable, 4 enterprise-capable, 5 leading.

| Dimension | Score | Rationale |
|---|---:|---|
| Work Management | 2.0 | Rich UI and core fields; defective lifecycle, hierarchy, ownership, and concurrency. |
| Project Management | 1.0 | Project is mainly external label/master data, not a governed container. |
| Agile/Sprint Management | 1.0 | Basic screens exist; history, carry-forward, capacity, velocity, and burndown are unreliable. |
| Collaboration | 2.0 | Comments, mentions, watchers, files, and live updates; lifecycle and boundary gaps remain. |
| User Management | 1.5 | Basic admin flows; broken registration, status enforcement, and credential lifecycle. |
| Roles and Access Control | 1.0 | Coarse roles, inconsistent authorization, stale claims, and cross-team gaps. |
| Notifications | 1.5 | Several useful in-app events; incomplete coverage/preferences/integrity. |
| Email Automation | 1.0 | Event templates exist; delivery is lossy and operationally unmanaged. |
| Workflow Automation | 0.5 | Hardcoded cascade only; no governed automation engine. |
| Reporting and Dashboards | 1.0 | Presentational breadth, but source data and historical semantics are unreliable. |
| Search and Saved Views | 2.0 | Useful basics; limited query expressiveness and sharing/governance. |
| Auditability | 1.0 | Activity records exist, but material paths and immutable history are missing. |
| Integration Readiness | 0.5 | External lookup dependency, but no supported API, webhooks, imports, or event contract. |
| Administration | 1.5 | Master/team screens exist; role, setup, and policy inconsistencies remain. |
| Security | 1.0 | Sound cookie basics, but revocation, status, policy, IDOR, rate limiting, and headers are inadequate. |
| Scalability/Reliability | 1.0 | Some pagination/caching; in-memory jobs, full loads, denormalized ownership, and weak operations. |
| UX Quality | 2.5 | Visually ambitious and coherent unauthenticated surface; density, duplication, broken routes, and untested accessibility. |

## Comparison with mature platforms

This comparison is a maturity calibration, not a recommendation to clone every competitor feature.

| Area | Mature-platform baseline | Current gap |
|---|---|---|
| Workflow | Jira supports governed transitions with conditions, validators, post-functions, and triggers. | Hardcoded dictionaries conflict with configurable states; alternate endpoints bypass validation. |
| Work hierarchy | Jira/Azure Boards govern work-item types and parent-child semantics; ClickUp supports nested hierarchy/subtasks. | Hierarchy is mostly UI convention with no backend type policy or reliable rollup. |
| Sprint analytics | Azure Boards treats burndown, capacity, velocity, and forecast as integrated sprint concepts. | Current membership is mutable; there is no stable commitment/scope ledger. |
| Capacity | Azure Boards accounts for member availability, holidays, nonworking days, activities, remaining work, and capacity bars. | Manual sprint hours are compared with mixed-unit weight; days off and daily capacity are unused. |
| Flexible work management | ClickUp exposes customizable status, fields, hierarchy, views, templates, automations, dependencies, time tracking, and workload measures. | Views are comparatively strong, but model extensibility, automation, templates, time ledger, and workload semantics are absent. |
| Permissions | Mature products expose scoped permissions and re-evaluate access as membership changes. | Two system roles plus three team roles are embedded across code/UI; claims can become stale. |
| Integration | Mature products provide APIs, webhooks, import/export, SCM/CI links, and marketplace integrations. | No supported integration surface or durable domain event model. |

Reference baselines:

- Azure Boards capacity explicitly considers per-member hours, holidays, vacation, nonworking days, and capacity bars: <https://learn.microsoft.com/en-us/azure/devops/boards/sprints/set-capacity?view=azure-devops>
- Azure Boards sprint concepts include stable burndown/burnup, individual/team capacity, forecast, backlog, and velocity semantics: <https://learn.microsoft.com/en-us/azure/devops/boards/sprints/scrum-key-concepts?view=azure-devops>
- Jira advanced workflows describe conditions, validators, post-functions, and triggers: <https://support.atlassian.com/jira-cloud-administration/docs/configure-advanced-issue-workflows/>
- ClickUp documents customizable statuses, multiple assignees, time tracking, dependency warnings, and sprints: <https://help.clickup.com/hc/en-us/articles/9559764679831-Customizable-ClickUp-features>
- ClickUp documents hierarchy and nested subtasks: <https://help.clickup.com/hc/en-us/articles/13856392825367-Intro-to-the-Hierarchy>

## Missing core capabilities — implementation expectations

### 1. Immutable sprint ledger and historical reporting

**Expected behavior:** planning creates a committed scope; additions, removals, estimate changes, status changes, completion, and carry-forward are events. Completing a sprint freezes its summary while preserving links to moved successor assignments.

**Data model:** `Sprint.Status`, `CompletedAt`, `SprintWorkItem`, `SprintScopeEvent`, `SprintSnapshot`, and `CarryForwardFrom/To` or equivalent append-only ledger.

**Rules:** only one active sprint per team if that is the chosen policy; target cannot equal source; completed sprints cannot be edited or reactivated without an audited administrative correction; reports use snapshot/event time.

**UI:** planning baseline, scope-change badges, completion preview, explicit carry-forward destination, historical read-only sprint page, and reconciliation warnings.

**Reporting/notifications:** commitment vs completed vs carried-forward, added/removed scope, stable burndown/burnup, velocity, spillover, and completion notifications.

**Dependencies:** unified workflow command, normalized estimates, audit/event infrastructure, migration reconciliation.

### 2. Unified workflow engine

**Expected behavior:** every status change—form, drag, quick action, bulk, import, or API—invokes the same command.

**Data model:** workflow definition, state, transition, work-item-type mapping, required-field rule, permission rule, and versioned published configuration.

**Rules:** server-authoritative transitions, required fields, open-child/dependency guards, terminal/reopen effects, concurrency, override permission, and fully audited bulk handling.

**UI:** only valid actions displayed, clear validation reasons, workflow preview/admin publishing, accessible confirmation for overrides.

**Reporting/notifications:** state duration, transition frequency/failures, reopen rate, SLA breaches, and event-driven notifications.

**Dependencies:** authorization policies, command/event model, repaired work states.

### 3. Normalized assignment and workload model

**Expected behavior:** users are assigned by immutable user ID; multiple assignees have explicit allocation; removal/deactivation preserves historic attribution.

**Data model:** authoritative `TaskAssignment` with user, allocation/role, dates, assigned-by, and active state; optional immutable assignment events.

**Rules:** assignees must be eligible members unless an explicit cross-team permission exists; assignment changes use `CanAssign`; no display-name identity joins.

**UI:** searchable eligible member picker, allocation totals, inactive/historic identity badges, reassignment reason.

**Reporting/notifications:** workload by user/allocation, assignment churn, unassigned work, and reliable assignment events.

**Dependencies:** data recovery, user lifecycle, sprint ledger.

### 4. Enterprise authorization and identity lifecycle

**Expected behavior:** account deactivate, membership removal, and role downgrade revoke effective access promptly. Permissions are explicit by resource/action.

**Data model:** security version/session records, permission/role mappings, audit records, optional enterprise identity linkage.

**Rules:** active-account check, current membership check, policy evaluation, separation of view/edit/assign/admin/override/delete/export, and nondisclosing cross-team failures.

**UI:** role-permission matrix, active sessions, revoke action, access-denied guidance, optional SSO/MFA configuration.

**Reporting/notifications:** security changes, privileged operations, failed access, role drift, and session revocation events.

**Dependencies:** policy inventory, threat model, integration tests.

### 5. Durable notification and email outbox

**Expected behavior:** domain transaction records a notification/outbox event; workers deliver at least once with idempotent consumers and visible status.

**Data model:** outbox event, recipient delivery, attempt, next-attempt, provider result, idempotency key, dead-letter state, preference subscription.

**Rules:** bounded retries/backoff, deduplication, recipient eligibility, tenant scope, quiet hours/digests, retention, and PII-safe logs.

**UI:** per-event preferences, delivery history, admin retry/cancel, queue/dead-letter health.

**Reporting/notifications:** success latency/rate, failure reasons, backlog age, retries, and alerts.

**Dependencies:** domain events, worker ownership, operational telemetry.

### 6. Append-only time and estimate tracking

**Expected behavior:** users add/edit/correct time entries under policy; estimates and remaining work are distinct; sprint analytics use dated entries and historical assignment.

**Data model:** `TimeEntry` with work item, user, work date, duration, note, source, approval/correction metadata; estimate history.

**Rules:** authorization, day/date limits, correction rather than destructive overwrite for closed periods, sprint attribution at entry time.

**UI:** quick log, timer optional, daily/weekly entry, remaining-work update, correction workflow.

**Reporting/notifications:** planned vs actual, burn rate, user/team utilization, missing entries, and approval exceptions.

**Dependencies:** normalized users/assignments, sprint ledger, reporting semantics.

### 7. First-class projects and governed master data

**Expected behavior:** a project owns work, teams, lifecycle, lead, dates, health, permissions, and optional releases; external master data synchronizes without breaking local integrity.

**Data model:** Project, ProjectTeam, lifecycle state, external mapping, sync status, release/version.

**Rules:** external IDs map through validated integration tables; unavailable masters degrade gracefully; project access constrains all work.

**UI:** project overview, health, teams, backlog/roadmap, sync diagnostics, archive.

**Reporting/notifications:** project delivery health, scope, schedule, release progress, sync failures.

**Dependencies:** product decision on external HIM authority, permission model, integration contract.

### 8. Public API, webhooks, and import framework

**Expected behavior:** supported versioned APIs and webhooks use the same commands/policies as UI; imports are validated, previewed, idempotent, and auditable.

**Data model:** API client/token scope, webhook subscription/delivery, import job/row result, external key mapping.

**Rules:** least privilege, rate limits, idempotency, replay protection, pagination, stable error contract, tenant scope.

**UI:** integration administration, webhook delivery/replay, import mapping/preview/errors.

**Reporting/notifications:** usage, failures, latency, webhook backlog, import reconciliation.

**Dependencies:** unified commands, authorization, durable events, observability.

## Prioritized roadmap

### Phase 0 — Release containment and data recovery

- Freeze production owner/sprint migrations and disable unsafe automatic schema bootstrap.
- Back up and reconcile assignments, team roles, inactive sprints, external IDs, and orphans.
- Fix the TeamLead constraint and patch vulnerable MailKit/MimeKit versions.
- Put tests into the solution/CI and restore a compiling baseline.
- Block or feature-flag unsafe bulk/full-edit status and owner changes until unified rules exist.

### Phase 1 — Integrity and security foundation

- Normalize assignment identity and repair data.
- Introduce policy-based authorization, active-account enforcement, claim/session revocation, and rate limiting.
- Close cross-team link/stat/mention paths and enforce relational integrity.
- Centralize task commands, concurrency, audit, notifications, and transactions.
- Establish security headers, upload scanning/isolated storage, health checks, and structured telemetry.

### Phase 2 — Correct work and sprint semantics

- Replace hardcoded/configured status split with a versioned workflow model.
- Enforce hierarchy type rules, completion/reopen rollups, dependency rules, and estimate semantics.
- Add immutable sprint membership/events/snapshots and safe carry-forward.
- Add append-only time entries, remaining work, days off, and unit-consistent capacity.
- Rebuild burndown, velocity, spillover, and workload reports from historical data.

### Phase 3 — Enterprise management capabilities

- First-class projects/releases and project-scoped permissions.
- Templates, checklists, recurring work, governed custom fields, and automation rules.
- Durable outbox and notification preferences/operations.
- API, webhooks, import/export, and integration administration.
- Refactor the Tasks UI into tested components/partials and complete accessibility/responsive remediation.

### Phase 4 — Scale and differentiation

- Portfolio roadmap, cross-team dependency views, forecasting, SLA/service workflows, and advanced analytics where validated by users.
- SSO/SCIM, audit export/retention controls, and compliance features appropriate to target customers.
- Do not pursue whiteboards, AI assistance, marketplace breadth, or financial PPM until the core system-of-record guarantees are proven.

## Development backlog

Every item below is independently trackable; sequence reflects dependency order.

| Seq | Title | Category | Priority | Rationale | Impacted modules | Dependencies | Acceptance criteria | Complexity |
|---:|---|---|---|---|---|---|---|---|
| 1 | Freeze and reconcile owner migrations | Data integrity | Critical | Current sequence can erase assignment identity. | Data, Services, migration runbook | Verified backup | Recovery report accounts for every task; no destructive contract step before verified backfill; rollback rehearsed. | XL |
| 2 | Correct TeamLead constraint | Data integrity | Critical | DB and application role values conflict. | Data migrations, Teams | None | Forward migration permits exactly supported values; existing rows validated; create/edit tests pass for all roles. | S |
| 3 | Add tests to solution and repair fixtures | Quality | Critical | No tests currently execute. | Tests, solution, CI | None | `dotnet test` runs from CI; obsolete constructors fixed; failure makes pipeline fail; no stale binary false-green. | M |
| 4 | Centralize status-change command | Work management | Critical | Alternate paths bypass invariants. | Task services/controllers/UI | Test baseline | Save, drag, quick, bulk, and future API call one command; transitions, hours, children, dependencies, audit, notifications, concurrency are consistent. | XL |
| 5 | Enforce active accounts and revoke sessions | Security | Critical | Deactivated/downgraded users retain access. | Account, auth middleware, User/Team services | Security design | Non-active login denied; membership/role changes invalidate sessions promptly; automated revocation tests pass. | L |
| 6 | Close cross-team task IDOR paths | Security | Critical | Link/stat traversal crosses team boundary. | Task queries/writes/controllers | Policy helpers | Both link endpoints and every hierarchy traversal validate active team; cross-team tests reveal neither data nor existence. | M |
| 7 | Replace owner text with authoritative assignments | Work/data | Critical | Identity, reporting, permissions, and notification use names. | Data, Task/User/Dashboard/Sprint services, UI | Item 1 | All writes use user IDs; multi-owner behavior defined; legacy string read-only during migration then removed; reconciliation is zero-diff. | XL |
| 8 | Harden migration execution | Operations | Critical | Dual raw-DDL/EF path can partially upgrade. | Web startup, Data, deployment | Items 1–2 | Production uses one authoritative migration mechanism; failure stops deployment; pre/post checks and rollback documented. | L |
| 9 | Build immutable sprint ledger | Agile | Critical | History and analytics are mutable. | Data, Sprint/Task services | Items 4, 7 | Commitment, scope changes, completion, and carry-forward preserved; completed sprint read-only; historic reports remain unchanged after moves. | XL |
| 10 | Durable email/notification outbox | Reliability | High | Current queue silently loses events. | Services, Data, Web admin | Domain event contract | Events survive restart; retries/backoff/idempotency/dead letter work; operators can inspect and replay; queue metrics alert. | XL |
| 11 | Repair workflow-state model | Work management | High | Configured states conflict with hardcoded maps. | Master, Task services/UI, Data | Item 4 | Published workflow defines valid states/transitions per type; every current task maps; no dead-end state unless intentionally terminal. | XL |
| 12 | Enforce hierarchy policy and reopen rollup | Work management | High | Client-only type rules and stale closed parents corrupt progress. | Task services/UI/reports | Items 4, 11 | Server rejects invalid parent types/cycles; close and reopen effects are defined, audited, and tested through multiple levels. | L |
| 13 | Make dependencies enforceable and cycle-safe | Work management | High | `Blocks` is informational and can cycle. | Task links, workflow, UI | Items 4, 6 | Link types governed; cycles rejected; blocked completion behavior configurable; cross-team links forbidden; graph tests pass. | L |
| 14 | Implement real concurrency control | Data/work | High | RowVersion does not protect stale forms. | Task models/controllers/UI/services | Item 4 | Original token round-trips; stale single and bulk edits return conflict with reload/compare UX; automated concurrency tests pass. | M |
| 15 | Create append-only time entries | Time tracking | High | Mutable totals cannot support audit or sprint reporting. | Data, Task/Sprint services/UI | Items 7, 9 | Dated user entries sum to actuals; corrections audited; sprint/user attribution preserved; permissions and reporting tests pass. | XL |
| 16 | Unify estimates, remaining work, and capacity units | Agile | High | Weight/story points are compared with hours. | Data, Sprint/Dashboard/Task UI | Items 9, 15 | Estimate units explicit; remaining work separate; capacity uses hours/days consistently; multi-owner allocation does not double-count. | XL |
| 17 | Add days-off and effective capacity | Agile | High | Existing daily capacity/working days are unused. | User/Sprint services/UI | Item 16 | Sprint capacity derives working days minus leave/holidays; overrides audited; overload warning and reports use effective capacity. | L |
| 18 | Rebuild sprint analytics | Reporting | High | Burndown/velocity are not historically stable. | Sprint, Charts, Dashboard | Items 9, 15–17 | Burndown/burnup, velocity, scope change, spillover, and forecast use immutable time-aware facts and match test fixtures. | XL |
| 19 | Replace coarse role checks with policies | Security | High | Permission semantics are duplicated/inconsistent. | All controllers/services/UI | Items 5–7 | View/edit/assign/manage/override/delete/export policies documented and centrally tested for each role/team condition. | XL |
| 20 | Secure attachments | Security | High | Web-root storage and extension-only validation are weak. | Task attachments, hosting/storage | Policy model | Files stored outside executable web root/object storage; signature scanning/quarantine; authorized streaming; size/type/retention audit. | L |
| 21 | Add rate limiting and authentication protections | Security | High | Password login has no abuse controls. | Account, Program/config | Item 5 | Per-account/IP throttling and lockout policy; generic errors; security events/alerts; recovery/admin unlock; tests cover bypass attempts. | M |
| 22 | Establish security-header baseline | Security | High | Browser protection is incomplete. | Web middleware/layout/assets | CDN decision | CSP, HSTS in production, referrer/permissions policies and frame protection verified; external assets comply; security scan documented. | M |
| 23 | Repair registration and user onboarding | User management | Medium | UI-only registration and inconsistent password-change policy. | Account, Master, User service/views | Items 5, 19 | Product decision documented; enabled flow creates pending account or UI removed; approval/invite/forced reset/status all work and are audited. | L |
| 24 | Align sprint/team/activity role journeys | UX/access | Medium | Manuals, UI, controller, and service disagree. | Controllers/services/views/docs | Item 19 | One role matrix drives UI and backend; Member/Lead/Admin journeys match documentation; direct URL tests pass. | M |
| 25 | Add database relational constraints | Data integrity | High | Multiple logical references can orphan. | Data migrations/services | Data cleanup | Orphans reconciled; required FKs/indexes installed with intended delete behavior; concurrency/delete integration tests pass. | L |
| 26 | Replace `local_setup.sql` with supported provisioning | Operations | High | Current script is stale/insecure. | Data, docs, tooling | Item 8 | Fresh environment provisioned solely from supported mechanism; schema matches model; no sample secrets; smoke test passes. | M |
| 27 | Patch vulnerable mail dependencies | Security | High | NuGet reports moderate advisories. | Services/Web packages | Compatibility test | Upgrade to non-vulnerable versions; NuGet audit clean; SMTP integration tests pass. | S |
| 28 | Add health, metrics, and correlation | Operations | High | Failures and backlogs are hard to detect. | Web, Services, deployment | Items 8, 10 | Liveness/readiness, DB/outbox health, correlation IDs, structured command logs, dashboards and alerts documented/tested. | L |
| 29 | Optimize large-team queries | Performance | Medium | Several views materialize entire teams. | Task queries, Dashboard/Charts, UI | Correct data model | Bounded/paginated endpoints; server aggregation; representative load test meets agreed p95 and memory limits. | XL |
| 30 | Refactor Tasks UI and remove duplicate code | Maintainability | Medium | Active 4,748-line page duplicates inactive partials/JS. | Razor, JS, CSS | Regression browser tests | One implementation per component; feature parity; bundle/payload reduced; unit/browser tests cover key interactions. | XL |
| 31 | Accessibility and responsive remediation | UX | Medium | Complex modals/drag behavior need keyboard and screen-reader support. | All active views/components | Item 30 | WCAG 2.2 AA target agreed; keyboard equivalents, focus/announcements, contrast and responsive journeys pass automated/manual checks. | L |
| 32 | Make projects first-class | Project management | Medium | External labels cannot govern delivery/access. | Data, services, navigation, reports | Items 7, 19, integration decision | Project lifecycle/owner/team/access/health defined; tasks migrate; archive and project reporting work; external mappings reconciled. | XL |
| 33 | Add templates, checklists, and recurring work | Productivity | Medium | Repetitive operational work is unsupported. | Data, Task services/UI, scheduler | Items 4, 10, 32 | Governed templates; checklist rollup; recurrence idempotently creates work; permissions/audit/notifications consistent. | XL |
| 34 | Add versioned API, webhooks, and import | Integration | Medium | Enterprise interoperability is absent. | New API/integration modules | Items 4, 10, 19, 28 | Versioned scoped API; pagination/idempotency/rate limits; webhook replay; import preview/reconciliation; contract/security tests. | XL |
| 35 | Add governed custom fields and automation | Extensibility | Medium | Hardcoded model cannot cover diverse teams. | Data, admin, workflow, queries/reports | Items 11, 19, 34 | Typed fields with scope/index rules; automation conditions/actions/retries/audit; search/report exposure; safe limits. | XL |

## Verification record

| Check | Result |
|---|---|
| `dotnet restore src/TaskTracker.slnx` | Passed |
| `dotnet build src/TaskTracker.slnx --no-restore` | Passed, 0 warnings, 0 errors |
| `dotnet restore src/TaskTracker.Tests/TaskTracker.Tests.csproj --force` | Passed |
| `dotnet test ... --no-restore` | **Failed to compile**; obsolete test helper constructors and email dependency mismatch |
| EF pending model changes | None reported; decimal precision warning for `DailyCapacityHours` |
| NuGet vulnerability audit | Moderate advisories for MailKit 4.8.0 and MimeKit 4.8.0 |
| npm production dependency audit | 0 vulnerabilities |
| Local application startup | Passed with schema bootstrap disabled and non-production secret placeholders |
| Playwright login page | Rendered successfully at desktop and 390×844 mobile sizes |
| `/Account/Register` unauthenticated request | Redirects to login; no controller action found |
| Authenticated System Admin routes | 17 primary routes/views rendered HTTP 200 without a visible server-error page |
| Authenticated Member task lifecycle | Create, watch, comment, attach, retrieve, transition, full-edit, history, delete, and cleanup executed |
| Role/membership revocation | Executed with multiple browser contexts; stale Sprint and Tasks access reproduced |
| Cross-team isolation | Direct task read/search denied; cross-team link creation and metadata disclosure reproduced |
| Sprint completion/history | Same-target, repeated completion, post-completion edit, backlog move, and deletion reproduced |
| In-app assignment notification | Passed for create-with-owner; failed for `QuickSetOwner` path |
| SMTP attempt/failure logging | Passed safely with `example.invalid` recipient and placeholder credentials |
| Successful end-to-end SMTP delivery | **NOT TESTED — safe working SMTP credentials were not supplied** |
| Team Lead journey | **BLOCKED — assigning `TeamLead` returns HTTP 500 due to DB constraint** |

## Final verdicts

### Production readiness

**NOT READY — CRITICAL RISK.** The system should not be promoted as the authoritative enterprise tracker until assignment data is reconciled, lifecycle commands are unified, team/account revocation is effective, cross-team paths are closed, tests execute, migrations are controlled, and sprint history is immutable.

### Enterprise maturity

**EARLY PRODUCT.** The product is beyond a toy or static prototype: it has real domain breadth and a strong visual work surface. Its maturity is constrained by system-of-record guarantees rather than feature count. It is several foundational releases away from a dependable Jira/Azure Boards/ClickUp-class core. The shortest credible path is to narrow the initial enterprise promise to secure team work management and correct sprint tracking, then expand only after integrity, audit, and operations are proven.

### Residual audit work

Residual work is limited to journeys that could not safely or technically complete in this environment: repair the role constraint and run a genuine Team Lead session; verify successful SMTP delivery with a safe mailbox; add a second ordinary-team account for notification preference/mention recipient testing; run destructive concurrency races in parallel browser sessions; and execute production-sized performance, accessibility, security-header, upload-scanning, restart/retry, and queue-saturation tests. The runtime audit used disposable records and removed all audit users, memberships, tasks, sprints, links, attachments, notifications, email logs, watchers, and related activity rows when finished.
