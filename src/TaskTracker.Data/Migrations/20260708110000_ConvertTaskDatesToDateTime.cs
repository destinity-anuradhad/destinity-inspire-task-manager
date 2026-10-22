using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations;

public partial class ConvertTaskDatesToDateTime : Migration
{
    // Converts five nvarchar date columns on Tasks to datetime2.
    // Guards handle both fresh DBs (column absent) and existing DBs (column is nvarchar).
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var col in new[] { "TargetDate", "StartDate", "EndDate", "ActualStartDate", "ActualEndDate" })
        {
            var idxName = col is "TargetDate" or "StartDate" ? $"IX_Tasks_{col}" : null;

            migrationBuilder.Sql($@"
                IF EXISTS (
                    SELECT 1 FROM sys.columns c
                    JOIN sys.types t ON c.user_type_id = t.user_type_id
                    WHERE c.object_id = OBJECT_ID(N'Tasks') AND c.name = N'{col}'
                      AND t.name IN ('nvarchar','varchar')
                )
                BEGIN
                    -- Drop index if it exists (nvarchar-indexed column)
                    {(idxName != null ? $@"IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'Tasks') AND name = N'{idxName}')
                        DROP INDEX [{idxName}] ON [Tasks];" : "-- no index")}

                    -- Add typed column, migrate data, swap
                    ALTER TABLE [Tasks] ADD [{col}_new] DATETIME2 NULL;
                    UPDATE [Tasks] SET [{col}_new] = TRY_CONVERT(DATETIME2, [{col}], 23)
                        WHERE [{col}] IS NOT NULL AND [{col}] != '';
                    ALTER TABLE [Tasks] DROP COLUMN [{col}];
                    EXEC sp_rename N'Tasks.{col}_new', N'{col}', N'COLUMN';

                    -- Recreate index as datetime2
                    {(idxName != null ? $"CREATE INDEX [{idxName}] ON [Tasks] ([{col}]);" : "-- no index")}
                END
                ELSE IF NOT EXISTS (
                    SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Tasks') AND name = N'{col}'
                )
                BEGIN
                    ALTER TABLE [Tasks] ADD [{col}] DATETIME2 NULL;
                    {(idxName != null ? $"CREATE INDEX [{idxName}] ON [Tasks] ([{col}]);" : "-- no index")}
                END
                -- else: column already datetime2, nothing to do
            ");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reversing datetime2 → nvarchar is lossy; provided for completeness only.
        foreach (var col in new[] { "TargetDate", "StartDate", "EndDate", "ActualStartDate", "ActualEndDate" })
        {
            var idxName = col is "TargetDate" or "StartDate" ? $"IX_Tasks_{col}" : null;

            migrationBuilder.Sql($@"
                IF EXISTS (
                    SELECT 1 FROM sys.columns c
                    JOIN sys.types t ON c.user_type_id = t.user_type_id
                    WHERE c.object_id = OBJECT_ID(N'Tasks') AND c.name = N'{col}'
                      AND t.name = 'datetime2'
                )
                BEGIN
                    {(idxName != null ? $@"IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'Tasks') AND name = N'{idxName}')
                        DROP INDEX [{idxName}] ON [Tasks];" : "")}
                    ALTER TABLE [Tasks] ADD [{col}_old] NVARCHAR(450) NULL;
                    UPDATE [Tasks] SET [{col}_old] = CONVERT(NVARCHAR(10), [{col}], 23)
                        WHERE [{col}] IS NOT NULL;
                    ALTER TABLE [Tasks] DROP COLUMN [{col}];
                    EXEC sp_rename N'Tasks.{col}_old', N'{col}', N'COLUMN';
                    {(idxName != null ? $"CREATE INDEX [{idxName}] ON [Tasks] ([{col}]);" : "")}
                END
            ");
        }
    }
}
