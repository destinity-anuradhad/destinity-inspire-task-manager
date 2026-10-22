# Load testing (backlog #29)

Backlog item #29 asks for **bounded/paginated endpoints and server-side aggregation that meet an
agreed p95 and memory limit under representative load.** The query code is already optimized (composite
covering indexes, SQL-side aggregation, API pagination); what remains is to **measure** it at
production-scale volume. This package provides everything needed except the run itself, which requires
a real (non-production) deployment.

## Artifacts

| File | Purpose |
|---|---|
| [`tools/loadtest/seed-loadtest-data.sql`](../tools/loadtest/seed-loadtest-data.sql) | Seeds one large team (default 200 users, 50k tasks) so the heavy read paths have realistic volume. |
| [`tools/loadtest/tasktracker-load.js`](../tools/loadtest/tasktracker-load.js) | [k6](https://k6.io) script exercising the v1 API read paths under ramping load. |

## Proposed SLOs (confirm before treating as pass/fail)

| Endpoint | Metric | Target |
|---|---|---|
| `GET /api/v1/tasks` (paginated list) | p95 latency | **< 800 ms** |
| `GET /api/v1/tasks/{id}` (detail) | p95 latency | **< 500 ms** |
| `GET /api/v1/projects` | p95 latency | **< 800 ms** |
| `GET /api/v1/tasks/export` (full CSV) | p95 latency | **< 3 s** |
| All | error rate (non-2xx) | **< 1%** |
| Server process | working set | **agree a ceiling** (e.g. < 1 GB at steady state) — watch it during the run |

These are encoded as k6 `thresholds`; a run fails if they're breached. Adjust them in the script's
`options.thresholds` once you've agreed the real targets.

## How to run

1. **Provision a non-production deployment** at production-representative resources, pointed at a
   throwaway database.
2. **Seed volume.** With the app stopped:
   ```bash
   sqlcmd -S <server> -d <perfdb> -U <user> -i tools/loadtest/seed-loadtest-data.sql
   ```
   Edit `@Confirm = 'YES'` and the `@UserCount` / `@TaskCount` at the top first. It tags everything it
   creates (`LOADTEST Team`, `lt_` usernames, `loadtest-seed` tasks) and includes a cleanup block.
3. **Mint an API key.** Sign in as an admin of the *LOADTEST Team* → **Admin → API Keys** → create a
   key with scopes `projects:read` + `tasks:read`. Copy the full `tt_xxx.yyy` value.
4. **Handle rate limiting** (important — see below). Either raise/disable the `api` policy in the perf
   environment, or mint several keys and pass them comma-separated as `API_KEYS`.
5. **Run k6:**
   ```bash
   k6 run -e BASE_URL=https://staging.example.com -e API_KEY=tt_xxx.yyy \
          -e VUS=100 -e DURATION=3m tools/loadtest/tasktracker-load.js
   ```
6. **Record** the k6 summary (p95 per endpoint, error rate, `rate_limited_429`) and the server's
   peak/steady memory + CPU from your host metrics. Compare against the SLOs.

## Rate limiting caveat

The integration API is throttled to **120 req/min per key** (policy `api` in `Program.cs`). Under load
that limiter will return **HTTP 429** and you'll be measuring the throttle, not the queries. The script
counts 429s separately (`rate_limited_429`) so this is visible. For a clean query-perf measurement:

- **Preferred:** temporarily raise or disable the `api` rate-limit policy in the perf environment only.
- **Alternative:** mint N keys and pass `-e API_KEYS=key1,key2,...` — the limiter partitions per key, so
  M keys give ~M×120 req/min of headroom.

## Interpreting results / next steps

- **If SLOs pass at target load** → #29's acceptance ("meets agreed p95 and memory limits") is met;
  record the numbers and close it.
- **If a path regresses** (most likely the list or export at high page numbers) → capture the actual
  SQL (SQL Server Query Store / an Extended Events session during the run), confirm index usage, and
  optimize that specific query. Re-run to confirm.

## Scope note

This measures the **API** read paths, which are the cleanest representation of the "materialize entire
team" queries. The MVC dashboard/charts pages share the same underlying services and indexes; if you
want to load them directly you'll need a cookie-auth flow (login + antiforgery), which is out of scope
for this first harness.
