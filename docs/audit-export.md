# Audit export & retention

A compliance-oriented capability (Phase 4) over the `ActivityLog` audit trail: **System Administrators**
can export the trail for external review and enforce a **retention window**. All actions are
System-Admin only (`Policies.SystemAdmin`) and surfaced on the **Activity** page.

## Export

**Admin → Activity → Export CSV** downloads the audit trail as CSV, honouring whatever filters are
active on the page (category, user, date range, team). Backed by `GET /Activity/Export`.

- Columns: `Id, ChangedAtUtc, ChangedBy, Category, Action, Details, TaskId, TeamId, Project, IpAddress`.
- Values are RFC-4180 escaped (quotes/commas/newlines safe).
- A single export is capped at **100,000 rows** to bound memory; the export itself is audited, and the
  audit note records if the result was truncated (narrow the date range to export more).
- The export action is itself recorded in the audit log (who exported, how many rows).

## Retention

**Admin → Activity → Retain _N_ days → Purge older** permanently deletes audit entries older than the
window. Backed by `POST /Activity/Purge` (antiforgery-protected, requires an explicit confirm).

- **Preview first (optional):** `GET /Activity/RetentionPreview?retainDays=N` returns the cutoff and how
  many entries would be removed — read-only, changes nothing.
- The purge **writes its own audit entry** (`Audit Retention Purge` — who, cutoff, count), so the fact
  that a retention purge happened is never itself erased silently. That record is younger than the
  cutoff, so it always survives.
- Deletion is permanent. The UI requires a typed retention window and a confirmation dialog.

## Notes & extension points

- **Scheduling:** purge is deliberately **admin-triggered**, not a background job — an unattended
  destructive sweep over an audit trail is a decision, not a default. To automate it, add a hosted
  service that calls `IAuditExportService.PurgeOlderThanAsync` on a schedule driven by a config value
  (e.g. `Audit:RetentionDays`), and log each run.
- **JSON export / SIEM:** `IAuditExportService.ExportAsync` returns structured `AuditExportRow`s; a JSON
  or syslog/SIEM feed can be added alongside the CSV without touching the query logic.
- **Tamper-evidence:** for stronger compliance, a future enhancement could hash-chain entries; today the
  guarantee is that deletions are themselves audited.
