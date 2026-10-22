using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TaskTracker.Data;

#nullable disable

namespace TaskTracker.Migrations
{
    /// <summary>Make existing and future task estimates use hours.</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261019140000_UseHoursForEstimates")]
    public partial class UseHoursForEstimates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE [Tasks] SET [EstimateUnit] = N'Hours' WHERE [EstimateUnit] IS NULL OR [EstimateUnit] <> N'Hours';
UPDATE [TaskTemplates] SET [EstimateUnit] = N'Hours' WHERE [EstimateUnit] IS NULL OR [EstimateUnit] <> N'Hours';

DECLARE @constraint sysname, @dropSql nvarchar(max);
SELECT @constraint = dc.[name]
FROM sys.default_constraints dc
JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
WHERE dc.[parent_object_id] = OBJECT_ID(N'[Tasks]') AND c.[name] = N'EstimateUnit';
IF @constraint IS NOT NULL
BEGIN
    SET @dropSql = N'ALTER TABLE [Tasks] DROP CONSTRAINT ' + QUOTENAME(@constraint);
    EXEC sp_executesql @dropSql;
END
ALTER TABLE [Tasks] ADD CONSTRAINT [DF_Tasks_EstimateUnit] DEFAULT N'Hours' FOR [EstimateUnit];

SET @constraint = NULL;
SET @dropSql = NULL;
SELECT @constraint = dc.[name]
FROM sys.default_constraints dc
JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
WHERE dc.[parent_object_id] = OBJECT_ID(N'[TaskTemplates]') AND c.[name] = N'EstimateUnit';
IF @constraint IS NOT NULL
BEGIN
    SET @dropSql = N'ALTER TABLE [TaskTemplates] DROP CONSTRAINT ' + QUOTENAME(@constraint);
    EXEC sp_executesql @dropSql;
END
ALTER TABLE [TaskTemplates] ADD CONSTRAINT [DF_TaskTemplates_EstimateUnit] DEFAULT N'Hours' FOR [EstimateUnit];");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Estimate units are now consistently hours; reverting the defaults would reintroduce
            // point estimates for new records, so the data conversion is intentionally retained.
        }
    }
}
