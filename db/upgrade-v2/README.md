# Upgrade v1 → v2 (schema-compare deploy)

Goal: `DestiniyInspire_TaskManager_v2` = **the same data as v1** with the **current app schema**.

## Why schema-compare (not EF migration replay)
v1's schema **diverged from the migration history**: its base tables were built by the
old **bootstrap**, only 9 EF migrations were ever recorded, and it already contains tables
(`SprintHoursLogs`, `Modules`) that migrations create *later*. There is **no point in the
migration chain that matches v1**, so replaying migrations fails (`object already exists` /
`invalid column`). (The repo migrations can't even build a DB from scratch — they assume the
bootstrap ran.)

So we diff v1's actual schema against a **reference at the current schema** and apply only the
genuinely-missing objects. That's what `sqlpackage` does — additive and non-destructive.

## Files (run in order)
| # | File | Purpose |
|---|------|---------|
| 1 | `01-prepare_v2.sql`    | Back-fill ~24 owners that live only in `TaskOwners` into `Tasks.Owner` (non-destructive) |
| 2 | `02-deploy-schema.ps1` | `sqlpackage` compare/deploy of `reference.dacpac` → `_v2` (DRY RUN, or `-Apply`) |
| 3 | `03-stamp-history.sql` | Mark all migrations applied so the app's startup `MigrateAsync()` is a no-op |
| 4 | `04-postmigrate_v2.sql` | (Optional) set legacy tasks' `EstimateUnit = 'Hours'` |
| – | `reference.dacpac`     | Extracted schema of a fully-migrated DB (the target schema) |

The deploy is **additive**: `DropObjectsNotInSource=false` and `BlockOnPossibleDataLoss=true`,
so nothing v1 has is dropped and it halts rather than lose data. v1 rows are untouched.

## Prerequisites
- `sqlpackage` (dotnet tool) — already installed at `~/.dotnet/tools/sqlpackage.exe`.
- A **clean** `_v2` restored from v1 (see step 0).

## 0. (If the earlier EF script was run) unjam + re-restore `_v2`
The failed `dotnet ef`/idempotent-SQL attempt leaves `_v2` half-changed and can hold an
**open transaction** (locks). As **sa** in SSMS:
```sql
-- find leftover sessions holding an open transaction on _v2
SELECT s.session_id, s.login_name, s.host_name, s.program_name, s.open_transaction_count
FROM sys.dm_exec_sessions s
JOIN sys.dm_tran_locks t ON t.request_session_id = s.session_id
WHERE t.resource_database_id = DB_ID('DestiniyInspire_TaskManager_v2')
GROUP BY s.session_id, s.login_name, s.host_name, s.program_name, s.open_transaction_count;
-- close that SSMS window (rolls back), or:  KILL <session_id>;
```
Then **drop and re-restore `_v2` from v1** so you start clean:
```sql
BACKUP DATABASE [DestiniyInspire_TaskManager] TO DISK='C:\bak\src.bak' WITH INIT, COPY_ONLY;
ALTER DATABASE [DestiniyInspire_TaskManager_v2] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [DestiniyInspire_TaskManager_v2];
RESTORE FILELISTONLY FROM DISK='C:\bak\src.bak';   -- get logical names
RESTORE DATABASE [DestiniyInspire_TaskManager_v2] FROM DISK='C:\bak\src.bak'
  WITH MOVE '<LogicalData>' TO 'C:\...\DestiniyInspire_TaskManager_v2.mdf',
       MOVE '<LogicalLog>'  TO 'C:\...\DestiniyInspire_TaskManager_v2_log.ldf', RECOVERY;
```

## 1. Prepare (owner back-fill)
```
sqlcmd -S 10.4.1.14 -U stpluser -P <password> -C -d DestiniyInspire_TaskManager_v2 -i 01-prepare_v2.sql
```

## 2. Deploy the schema — DRY RUN first, then apply
```powershell
# from db/upgrade-v2/
pwsh ./02-deploy-schema.ps1 -Password <password>          # DRY RUN → deploy-report.xml + deploy-preview.sql (review!)
pwsh ./02-deploy-schema.ps1 -Password <password> -Apply    # apply the schema
```

## 3. Stamp migration history
```
sqlcmd -S 10.4.1.14 -U stpluser -P <password> -C -d DestiniyInspire_TaskManager_v2 -i 03-stamp-history.sql
```

## 4. (Optional) post-migration cleanup
```
sqlcmd -S 10.4.1.14 -U stpluser -P <password> -C -d DestiniyInspire_TaskManager_v2 -i 04-postmigrate_v2.sql
```

## 5. Point the app at `_v2` and verify
```
ConnectionStrings__DefaultConnection =
  Server=10.4.1.14;Database=DestiniyInspire_TaskManager_v2;User Id=stpluser;Password=<password>;TrustServerCertificate=True;
```
Start the app → `MigrateAsync()` finds nothing pending. Check task counts match v1 and the new
tables/columns exist.

## Rollback
Drop `_v2` and re-restore from v1 (step 0). Source v1 is never modified.

## Regenerating reference.dacpac (if the schema changes)
```powershell
sqlpackage /Action:Extract `
  /SourceConnectionString:"<a fully-migrated DB>" `
  /TargetFile:reference.dacpac /p:ExtractAllTableData=false
```
