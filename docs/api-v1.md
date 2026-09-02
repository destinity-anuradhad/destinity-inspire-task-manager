# API v1

The v1 integration API accepts **either** an interactive application cookie **or** an API key.
Every request is scoped to a single team and enforces that team's boundary.

## Authentication

| Method | How | Authorization |
|---|---|---|
| Session cookie | The normal signed-in browser session | Caller's role + active team; full access to the endpoints below |
| API key | `X-Api-Key: <key>` header, or `Authorization: Bearer <key>` | The key's granted scopes, acting as the key's owner within its team |

An API key looks like `tt_ab12cd34.<secret>`. The part before the `.` is a public prefix used to
identify the key in listings and logs; the secret after it is shown **only once** at creation and is
stored solely as a salted hash — it cannot be recovered. Present the whole string on each request.

A key is rejected (`401`) when it is unknown, expired, revoked, its owner is inactive, or its owner is
no longer a member of the scoped team.

### Scopes

| Scope | Grants |
|---|---|
| `projects:read` | `GET /projects` |
| `tasks:read` | All task/comment/history/link/attachment/export **reads** |
| `tasks:write` | Create task, change status, log time, add comment |

A request whose key lacks the scope required by an endpoint returns `403`. Cookie sessions are not
scope-limited — they keep their normal role/team authorization.

### Managing keys

Team administrators and system administrators mint, list, and revoke keys under **Admin → API Keys**
(`/ApiClients`). Keys are owned by the creating user and scoped to the active team; revocation takes
effect immediately.

## Endpoints

| Method | Endpoint | Purpose | Scope |
|---|---|---|---|
| `GET` | `/api/v1/projects` | Active-team projects with releases | `projects:read` |
| `GET` | `/api/v1/tasks` | Filtered, paginated task list (`pageSize` max 100) | `tasks:read` |
| `GET` | `/api/v1/tasks/{id}` | A task visible to the active team | `tasks:read` |
| `GET` | `/api/v1/tasks/export` | Filtered task CSV export | `tasks:read` |
| `POST` | `/api/v1/tasks` | Create a task through the governed task command | `tasks:write` |
| `PATCH` | `/api/v1/tasks/{id}/status` | Apply a governed status transition | `tasks:write` |
| `POST` | `/api/v1/tasks/{id}/time-entries` | Log append-only work time | `tasks:write` |
| `POST` | `/api/v1/tasks/{id}/comments` | Add a task comment | `tasks:write` |
| `GET` | `/api/v1/tasks/{id}/comments` | Read task comments | `tasks:read` |
| `GET` | `/api/v1/tasks/{id}/history` | Read task audit history | `tasks:read` |
| `GET` | `/api/v1/tasks/{id}/links` | Read task relationships | `tasks:read` |
| `GET` | `/api/v1/tasks/{id}/attachments` | Read attachment metadata | `tasks:read` |

Write endpoints return `400` for validation or business-rule failures and do not bypass the service
layer. A non-visible task returns `404` from the detail endpoint.

## Operational notes

- **Rate limiting.** Requests are throttled to 120/minute, partitioned per API key (falling back to
  caller IP). Exceeding the limit returns `429`.
- **CSRF.** Antiforgery validation applies only to ambient cookie sessions. API-key requests use an
  explicit header (not a cookie) and are exempt, so unsafe methods work without a token.
- **Auth failures on `/api`** return status codes (`401`/`403`) rather than an HTML login redirect.

## Not yet implemented

Webhooks / event subscriptions, bulk import, and OAuth client-credential flows remain future work
(see the enterprise audit, backlog item 34).
