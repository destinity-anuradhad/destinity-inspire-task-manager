# API v1

All endpoints require the existing authenticated application cookie and enforce
the caller's active-team scope. They are not public APIs or token-authenticated
endpoints yet.

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `/api/v1/projects` | Active-team projects with releases |
| `GET` | `/api/v1/tasks` | Filtered, paginated task list (`pageSize` max 100) |
| `GET` | `/api/v1/tasks/{id}` | A task visible to the active team |
| `GET` | `/api/v1/tasks/export` | Filtered task CSV export |
| `POST` | `/api/v1/tasks` | Create a task through the governed task command |
| `PATCH` | `/api/v1/tasks/{id}/status` | Apply a governed status transition |
| `POST` | `/api/v1/tasks/{id}/time-entries` | Log append-only work time |
| `POST` | `/api/v1/tasks/{id}/comments` | Add a task comment |
| `GET` | `/api/v1/tasks/{id}/comments` | Read task comments |
| `GET` | `/api/v1/tasks/{id}/history` | Read task audit history |
| `GET` | `/api/v1/tasks/{id}/links` | Read task relationships |
| `GET` | `/api/v1/tasks/{id}/attachments` | Read attachment metadata |

Write endpoints return `400` for validation or business-rule failures and do
not bypass the service layer. A non-visible task returns `404` from the detail
endpoint.

External API clients, API keys, webhooks, and imports remain future work.
