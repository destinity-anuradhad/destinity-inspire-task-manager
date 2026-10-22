/*
  Run after the target database has been created from the current EF model.
  Copies every user table from the source database into target.Legacy.
  The source database is read only throughout this script.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @source sysname = N'DestiniyInspire_TaskManager';
DECLARE @target sysname = N'DestiniyInspire_TaskManager_v2';

IF DB_ID(@source) IS NULL OR DB_ID(@target) IS NULL
    THROW 51030, 'Source and target databases must both exist.', 1;

DECLARE @sql nvarchar(max) = N'USE ' + QUOTENAME(@target) + N';
    IF SCHEMA_ID(N''Legacy'') IS NULL EXEC(N''CREATE SCHEMA Legacy'');';
EXEC sys.sp_executesql @sql;

CREATE TABLE #SourceTables (TableName sysname NOT NULL PRIMARY KEY);
SET @sql = N'INSERT INTO #SourceTables (TableName)
    SELECT name FROM ' + QUOTENAME(@source) + N'.sys.tables
    WHERE is_ms_shipped = 0;';
EXEC sys.sp_executesql @sql;

DECLARE @table sysname;
DECLARE source_tables CURSOR LOCAL FAST_FORWARD FOR
    SELECT TableName FROM #SourceTables ORDER BY TableName;
OPEN source_tables;
FETCH NEXT FROM source_tables INTO @table;

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'IF OBJECT_ID(N''' + QUOTENAME(@target) + N'.Legacy.' + REPLACE(@table, '''', '''''') + N''', N''U'') IS NOT NULL
        THROW 51031, ''Legacy archive table already exists: ' + REPLACE(@table, '''', '''''') + N''', 1;
      SELECT * INTO ' + QUOTENAME(@target) + N'.Legacy.' + QUOTENAME(@table) + N'
      FROM ' + QUOTENAME(@source) + N'.dbo.' + QUOTENAME(@table) + N';';
    EXEC sys.sp_executesql @sql;
    FETCH NEXT FROM source_tables INTO @table;
END

CLOSE source_tables;
DEALLOCATE source_tables;
