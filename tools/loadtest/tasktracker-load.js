// k6 load test — backlog #29 (large-team query performance).
//
// Exercises the heavy "materialize entire team" read paths through the v1 API using an API key.
// Run against a NON-PRODUCTION deployment seeded with tools/loadtest/seed-loadtest-data.sql.
//
//   BASE_URL   e.g. https://staging.example.com   (no trailing slash)
//   API_KEY    a key minted under Admin -> API Keys, scoped to the LOADTEST team,
//              with scopes projects:read + tasks:read (see README).
//   VUS        virtual users (default 50)
//   DURATION   steady-state duration (default 2m)
//
// Example:
//   k6 run -e BASE_URL=https://staging.example.com -e API_KEY=tt_xxx.yyy \
//          -e VUS=100 -e DURATION=3m tools/loadtest/tasktracker-load.js
//
// IMPORTANT — rate limiting: the API is throttled to 120 req/min per key (policy "api"). A real
// load test will hit HTTP 429 and measure the limiter, not the queries. For the perf run either
// (a) raise/disable the "api" rate-limit policy in the perf environment, or (b) supply several keys
// via API_KEYS (comma-separated) so load spreads across partitions. 429s are tracked separately below.

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';

const BASE = (__ENV.BASE_URL || 'http://localhost:5000').replace(/\/$/, '');
const KEYS = (__ENV.API_KEYS || __ENV.API_KEY || '').split(',').map(s => s.trim()).filter(Boolean);
const VUS = parseInt(__ENV.VUS || '50', 10);
const DURATION = __ENV.DURATION || '2m';

const rateLimited = new Counter('rate_limited_429');
const listTrend = new Trend('dur_tasks_list', true);
const detailTrend = new Trend('dur_task_detail', true);
const exportTrend = new Trend('dur_tasks_export', true);
const projectsTrend = new Trend('dur_projects', true);

export const options = {
  scenarios: {
    steady: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: VUS },   // ramp up
        { duration: DURATION, target: VUS }, // steady state (measurement window)
        { duration: '20s', target: 0 },      // ramp down
      ],
      gracefulRampDown: '10s',
    },
  },
  thresholds: {
    // Proposed SLOs — confirm/adjust in README before treating as pass/fail.
    'http_req_failed': ['rate<0.01'],                 // < 1% errors
    'dur_tasks_list': ['p(95)<800'],                  // list p95 < 800ms
    'dur_task_detail': ['p(95)<500'],                 // detail p95 < 500ms
    'dur_projects': ['p(95)<800'],                    // projects p95 < 800ms
    'dur_tasks_export': ['p(95)<3000'],               // full CSV export p95 < 3s
  },
};

function key() {
  return KEYS.length === 0 ? '' : KEYS[(__VU + __ITER) % KEYS.length];
}

function get(path, trend) {
  const res = http.get(`${BASE}${path}`, { headers: { 'X-Api-Key': key() }, tags: { name: path.split('?')[0] } });
  trend.add(res.timings.duration);
  if (res.status === 429) rateLimited.add(1);
  check(res, { 'status 200': r => r.status === 200 });
  return res;
}

export function setup() {
  if (KEYS.length === 0) throw new Error('Set API_KEY (or API_KEYS) — see the file header.');
  // Grab a page of task ids so detail hits are realistic, not synthetic.
  const res = http.get(`${BASE}/api/v1/tasks?page=1&pageSize=100`, { headers: { 'X-Api-Key': KEYS[0] } });
  if (res.status !== 200) throw new Error(`Warm-up /api/v1/tasks failed: HTTP ${res.status} ${res.body}`);
  const ids = (res.json('data') || []).map(t => t.id);
  return { ids };
}

export default function (data) {
  // Weighted mix approximating a browsing team: mostly list + detail, occasional export.
  const roll = Math.random();
  if (roll < 0.55) {
    const page = 1 + Math.floor(Math.random() * 20);
    get(`/api/v1/tasks?page=${page}&pageSize=100`, listTrend);
  } else if (roll < 0.80 && data.ids.length) {
    const id = data.ids[Math.floor(Math.random() * data.ids.length)];
    get(`/api/v1/tasks/${id}`, detailTrend);
  } else if (roll < 0.95) {
    get(`/api/v1/projects`, projectsTrend);
  } else {
    get(`/api/v1/tasks/export`, exportTrend);   // heaviest path
  }
  sleep(Math.random() * 1.5 + 0.5);   // 0.5–2s think time
}
