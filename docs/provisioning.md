# Database provisioning

The **only** supported way to create or upgrade the schema is the EF Core migration set. Two options:

## Option A — apply migrations directly (dev / CI)

With the app stopped and `ConnectionStrings__DefaultConnection` pointing at the target database:

```bash
dotnet ef database update \
  --project src/TaskTracker.Data/TaskTracker.Data.csproj \
  --startup-project src/TaskTracker.Data/TaskTracker.Data.csproj
```

This applies exactly the migrations under `src/TaskTracker.Data/Migrations`, in order, and records
them in `__EFMigrationsHistory`.

## Option B — idempotent SQL script (controlled / production)

For environments where a DBA applies schema changes as a reviewed step, use the generated idempotent
script [`Migrations/schema-full.sql`](../src/TaskTracker.Data/Migrations/schema-full.sql). It guards
every migration with a check against `__EFMigrationsHistory`, so it is safe to run against an empty
database **or** one already at some migration — it only applies what's missing.

Regenerate it after adding migrations:

```bash
dotnet ef migrations script --idempotent \
  --project src/TaskTracker.Data/TaskTracker.Data.csproj \
  --startup-project src/TaskTracker.Data/TaskTracker.Data.csproj \
  -o src/TaskTracker.Data/Migrations/schema-full.sql
```

## Deprecated: `local_setup.sql`

The old hand-maintained `local_setup.sql` is **not** an authoritative source of schema and should not
be used to provision environments — it drifts from the model and carries no migration history. Prefer
Option A or B; `local_setup.sql` can be removed once no local workflow depends on it.

Neither script contains secrets; supply the connection string and any SMTP/reCAPTCHA secrets through
environment variables at runtime (see `Program.cs` `RequireSecret`).
