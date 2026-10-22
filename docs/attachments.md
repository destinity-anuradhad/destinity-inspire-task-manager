# Attachment security

Task attachments are handled so that a file can neither be reached without authorization nor be used
to smuggle executable content past the type filter.

## Storage outside the web root

Uploaded blobs are written to a directory **outside** the executable web root — by default
`App_Data/attachments/{taskId}/{storedName}` under the content root, overridable with
`Attachments:StoragePath`. Because that location is not under `wwwroot`, the static-file pipeline can
never serve it. As an extra guard, any direct request to the legacy `/uploads/attachments/*` path is
refused (`404`) in the middleware pipeline.

The result: **every** read goes through the authorized `GET /Tasks/DownloadAttachment/{id}` action,
which enforces the caller's active-team scope before streaming the file. Downloads are returned with a
`Content-Disposition: attachment` header (via a named `PhysicalFile`), so nothing renders inline in the
browser — closing stored-XSS-through-attachment vectors. `X-Content-Type-Options: nosniff` is set
globally.

Legacy files that still live under `wwwroot/uploads/attachments` remain **readable and deletable**
through the same authorized action (a back-compat fallback in `IAttachmentStorage`), but nothing new is
ever written there.

## Content signature validation

Uploads are checked twice:

1. **Extension allow-list** — only `.pdf .doc .docx .xls .xlsx .png .jpg .jpeg .gif .webp .zip .txt
   .csv .md` are accepted.
2. **Magic-byte signature** — the file's leading bytes must match a known signature for that extension
   (`FileSignatureValidator`). This rejects the classic bypass of renaming an executable or script to
   an allowed extension (e.g. an `MZ`/PE file called `photo.png` is refused). Plain-text formats
   (`.txt/.csv/.md`) have no binary signature and are accepted by extension.

A **10 MB** size limit is enforced before anything is written.

## Audit

Every upload and delete writes an `ActivityLog` entry (`Attachment Added` / `Attachment Removed`) with
the file name, size, and content type, attributed to the acting user and scoped to the task's team.

## Configuration

| Key | Default | Purpose |
|---|---|---|
| `Attachments:StoragePath` | `{ContentRoot}/App_Data/attachments` | Secure, non-web-root storage location. |

Point `Attachments:StoragePath` at a mounted volume or object-storage-backed path in production; the
directory should be backed up and access-controlled at the OS/storage layer.
