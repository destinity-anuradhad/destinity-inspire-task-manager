# API v1

The v1 integration API accepts **either** an interactive application cookie **or** an API key.
Every request is scoped to a single team and enforces that team's boundary.

The machine-readable description (OpenAPI 3) is at **`GET /api/v1/openapi.json`**. Fetch it with a
signed-in browser session or an API key; it documents every endpoint, filter, request body and response.

## Authentication

| Method | How | Authorization |
|---|---|---|
| Session cookie | The normal signed-in browser session | Caller's role + active team; full access to the endpoints below |
| API key | `X-Api-Key: <key>` header, or `Authorization: Bearer <key>` | The key's granted scopes, acting as the key's owner within its team |

An API key looks like `tt_ab12cd34.<secret>`. The part before the `.` is a public prefix used to
identify the key in listings and logs; the secret after it is shown **only once** at creation and is
stored solely as a SHA-256 hash, so it cannot be recovered. Present the whole string on each request.

A key is rejected (`401`) when it is unknown, expired, revoked, its owner is inactive, or its owner is
no longer a member of the scoped team.

### Scopes

| Scope | Grants |
|---|---|
| `projects:read` | `GET /projects` |
| `tasks:read` | All task, comment, history, link, attachment, time-entry, lookup and export **reads** |
| `tasks:write` | Create task, change status, log time, add comment |

A request whose key lacks the scope required by an endpoint returns `403`. Cookie sessions are not
scope-limited; they keep their normal role/team authorization.

### Own-tasks-only keys

A key can be limited to its owner's tasks (**Only my tasks**). Such a key:

- lists, reads and changes only tasks the owner is assigned to (`TaskOwners`); other tasks return `404`;
- can create tasks only for its owner (an `owner` naming someone else returns `403`);
- cannot use `GET /tasks/export` (`403`).

`GET /me` reports `ownTasksOnly`.

### Managing keys

- **Personal keys — every employee.** Profile menu → **My API Keys** (`/MyApiKeys`). A personal key
  acts as you in your active team, always expires (30 days to 1 year), is **Only my tasks** by default,
  and you can revoke it yourself. Up to 10 active keys per person.
- **Team keys — team and system administrators.** Admin → **API Keys** (`/ApiClients`) for integrations
  that need the whole team's tasks. Keys are owned by the creating user and scoped to the active team.

Revocation takes effect immediately. Key creation and revocation are written to the activity log.

## Endpoints

| Method | Endpoint | Purpose | Scope |
|---|---|---|---|
| `GET` | `/api/v1/me` | Who the caller is: user id, team, role, scopes, `ownTasksOnly`, server time zone | any |
| `GET` | `/api/v1/tasks` | Filtered, paginated task list (see below) | `tasks:read` |
| `GET` | `/api/v1/tasks/{id}` | A visible task, with notes, acceptance criteria and `rowVersion` | `tasks:read` |
| `GET` | `/api/v1/tasks/export` | Filtered task CSV export | `tasks:read` |
| `POST` | `/api/v1/tasks` | Create a task through the governed task command | `tasks:write` |
| `PATCH` | `/api/v1/tasks/{id}/status` | Apply a governed status transition | `tasks:write` |
| `POST` | `/api/v1/tasks/{id}/time-entries` | Log append-only work time | `tasks:write` |
| `GET` | `/api/v1/tasks/{id}/time-entries` | Time logged on a task | `tasks:read` |
| `GET` | `/api/v1/time-entries?from=&to=` | The caller's own time entries by work date (inclusive, ≤ 92 days) | `tasks:read` |
| `POST` | `/api/v1/tasks/{id}/comments` | Add a task comment | `tasks:write` |
| `GET` | `/api/v1/tasks/{id}/comments` | Read task comments | `tasks:read` |
| `GET` | `/api/v1/tasks/{id}/history` | Read task audit history | `tasks:read` |
| `GET` | `/api/v1/tasks/{id}/links` | Read task relationships | `tasks:read` |
| `GET` | `/api/v1/tasks/{id}/attachments` | Read attachment metadata | `tasks:read` |
| `GET` | `/api/v1/projects` | Active-team governed projects with releases | `projects:read` |
| `GET` | `/api/v1/modules` | Modules used by the team's tasks: `{id, name, taskCount}` | `tasks:read` |
| `GET` | `/api/v1/properties` | Properties (clients) used by the team's tasks: `{id, name, taskCount}` | `tasks:read` |
| `GET` | `/api/v1/sprints?active=true` | The team's sprints with start/end dates | `tasks:read` |

Write endpoints return `400` for validation or business-rule failures and do not bypass the service
layer. A task that is not visible to the caller returns `404`.

### `GET /tasks` filters

Lists are comma-separated and match any value.

| Parameter | Meaning |
|---|---|
| `ownerId` | Owner user ids, or `me`. **Exact** match on the task's owners. |
| `status` | e.g. `New,To Do,In Progress` |
| `itemType` | `Task`, `Bug`, `Story`, `Feature`, `Epic` |
| `priority` | e.g. `Critical,High` |
| `open` | `true` = not done or cancelled (per item type and workflow); `false` = done or cancelled |
| `sprint` / `sprintId` | A sprint id, `active` or `backlog` |
| `projectId`, `moduleId`, `propertyId` | Stable ids |
| `project`, `property` | Exact module / property name |
| `search` | Text in the goal, labels, module or property |
| `dueFrom`, `dueTo` | Due date range (`yyyy-MM-dd`) |
| `updatedSince` | Only tasks changed at or after this instant, e.g. `2026-10-21T03:50:00Z` |
| `ids` | Task ids, at most 200 |
| `includeDeleted` | Also return soft-deleted tasks (`isDeleted: true`) |
| `sort`, `sortDir` | `created` (default), `updated`, `due` or `id`; `asc` / `desc` (default `desc`) |
| `page`, `pageSize` | `pageSize` 1–100, default 20 |

Older parameters still work: `owner` (a loose match on display names; prefer `ownerId`),
`dateFrom`/`dateTo` (aliases of `dueFrom`/`dueTo`) and `sprint=__none__` (= `backlog`).

The response is `{ data, page, pageSize, totalCount, serverTime }`.

### Task fields

Each task carries the fields older clients use (`id`, `goal`, `status`, `priority`, `owner`, `project`,
`projectId`, `property`, `targetDate`, `startDate`, `endDate`, `weight`, `estimateUnit`, `actualHours`,
`remainingWorkHours`, `itemType`, `parentId`, `sprintId`, `teamId`, `createdAt`) plus:

| Field | Meaning |
|---|---|
| `ownerIds`, `owners` | The task's owners as user ids and `{id, displayName}`; `owner` stays as the display string |
| `statusCategory` | `todo`, `in_progress`, `blocked`, `done` or `cancelled`. Uses the task's workflow state category when it has one, otherwise the item type's rules. `Blocked` and `On hold` are `blocked`; `Rejected` is `cancelled`. |
| `dueDate` | The task form's **Due Date** (stored as `endDate`). `targetDate` is a legacy column the form no longer sets. |
| `sprint` | `{id, name, startDate, endDate, isActive}` |
| `moduleId`, `propertyId` | Stable ids for the module (`project` text) and property |
| `actualStartDate`, `actualEndDate` | When work started / finished |
| `updatedAt` | When anything on the task last changed (status, owners, fields, deletion) |
| `isDeleted` | Only `true` with `includeDeleted=true` |
| `webUrl` | The task's page, `{AppUrl}/Tasks?highlight={id}` |
| `reporter`, `labels` | As shown in the task form |

### Dates and time zones

- **Instants carry an offset.** `createdAt`, history and comment times are stored in the server's local
  time and returned with its offset (e.g. `2026-10-21T09:19:35+05:30`). `updatedAt`, `actualStartDate`,
  `actualEndDate` and time-entry `createdAt` are stored in UTC and returned with `Z`/`+00:00`.
- **Calendar dates have no time:** `dueDate`, `startDate`, `endDate`, `targetDate`, sprint dates and
  time-entry `workDate` are `yyyy-MM-dd`.
- `GET /me` returns `serverTimeZone` and `serverUtcOffset`.

### Incremental sync

1. First run: page through `GET /tasks?ownerId=me` and keep the first page's `serverTime`.
2. Then poll `GET /tasks?ownerId=me&updatedSince=<serverTime minus 2 minutes>` and keep the new
   `serverTime` each time. The overlap covers saves that were in flight; treat tasks you already have
   as updates.
3. To notice tasks deleted or reassigned away from you, occasionally ask for the tasks you hold by id
   (`ids=…&includeDeleted=true`, up to 200 per call). Ones that come back with `isDeleted: true`, or not
   at all, are no longer yours.

### Conditional requests (ETag)

`GET /tasks` returns a weak `ETag`. Send it back as `If-None-Match`; when nothing in the result has
changed, the response is `304 Not Modified` with no body, and the server only runs a single aggregate
query. The ETag reflects task changes; a renamed sprint or user alone does not change it.

### Creating tasks and logging time

`POST /tasks` body (only `goal` is required):

```json
{
  "goal": "Reply to Browns about the GRN rounding issue",
  "priority": "High",
  "dueDate": "2026-10-25",
  "weight": 1.5,
  "itemType": "Task",
  "moduleId": 10,
  "project": "Destinity Inspire - Inventory",
  "propertyId": 7,
  "owner": "Dev",
  "notes": "From email: …"
}
```

- `itemType` defaults to `Task`, and `status` to the item type's first status (`To Do` for a Task).
- `weight` is the estimate in hours. The task rules require one, in 10-minute steps.
- `priority` is `Critical`, `High`, `Medium` or `Low`. `owner` takes display names, comma-separated.
- The response is `201` with `{ "id": 123, "capacityWarning": null }`.

`POST /tasks/{id}/time-entries` body:

```json
{ "hours": 1.5, "workDate": "2026-10-21", "note": "Investigation", "remainingWorkHours": 2 }
```

`hours` must be greater than zero. Entries are append-only; corrections are separate negative entries.

### Idempotent writes

Send an `Idempotency-Key` header (any unique string per logical request, e.g. an email's message id)
on a write. The first call runs normally and its **successful** response is cached against your API key;
a retry with the same key replays that cached response (with `Idempotency-Replayed: true`) instead of
acting again, so a network retry or a double approval of `POST /tasks` won't create a duplicate. Failed
(non-2xx) responses are not cached, so you can safely retry them. Keys are scoped per API client and
kept indefinitely.

### Optimistic concurrency

`GET /tasks/{id}` returns a `rowVersion` (base64). Pass it back as `rowVersion` on
`PATCH /tasks/{id}/status` to guard against lost updates: if the task changed since you read it, the
request returns **`409 Conflict`** instead of silently overwriting. Omitting `rowVersion` keeps
last-write-wins behaviour.

## Operational notes

- **Rate limiting.** 120 requests per 60 seconds, partitioned per API key (falling back to caller IP).
  Every response carries `X-RateLimit-Limit: 120` and `X-RateLimit-Policy: 120;w=60`; a throttled
  request returns `429` with `Retry-After` (seconds).
- **CSRF.** Antiforgery validation applies only to ambient cookie sessions. API-key requests use an
  explicit header (not a cookie) and are exempt, so unsafe methods work without a token.
- **Auth failures on `/api`** return status codes (`401`/`403`) rather than an HTML login redirect.
- **Schema.** `Tasks.UpdatedAt` and `ApiClients.OwnTasksOnly` come from migration
  `20261022100000_AddApiIntegrationFields`. For an existing database, apply the reviewed additive script
  `tools/upgrades/2026-10-22-api-integration-fields.sql`
  (see [non-destructive-existing-database-upgrade.md](non-destructive-existing-database-upgrade.md)).

## Answers for integrators

| Question | Answer |
|---|---|
| Which field is the deadline? | `dueDate` (stored as `endDate`, the form's Due Date). `targetDate` is legacy. |
| What is a task's web address? | `webUrl`: `{AppUrl}/Tasks?highlight={id}`. |
| Which time zone are timestamps in? | Each instant now carries its offset (see "Dates and time zones"). |
| Why can `GET /projects` be empty? | It lists governed projects, which most teams have not set up yet; it also needs `projects:read`. Most tasks are grouped by module: use `GET /modules` and `moduleId`. |
| Is `weight` always hours? | Yes. Estimates are always in hours (`estimateUnit` is always `Hours`). |
| Which scopes exist? | `projects:read`, `tasks:read`, `tasks:write`, plus the own-tasks-only option. |
| Is there a rate limit? | 120 requests per minute per key, published in headers (see above). |

## Related

Outbound events are covered in [webhooks.md](webhooks.md) and bulk import in [import.md](import.md).
OAuth client-credential flows remain future work (see the enterprise audit, backlog item 34).
