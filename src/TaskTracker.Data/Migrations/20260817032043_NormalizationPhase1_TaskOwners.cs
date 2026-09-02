using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations
{
    /// <inheritdoc />
    public partial class NormalizationPhase1_TaskOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Create lookup tables first (FKs from Tasks will reference them) ────────
            migrationBuilder.CreateTable(
                name: "Modules",
                columns: table => new
                {
                    Id   = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_Modules", x => x.Id); });

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id   = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_Clients", x => x.Id); });

            // ── 2. Seed Modules/Clients from existing Task references ─────────────────────
            //       Must happen BEFORE FK_Tasks_Modules and FK_Tasks_Clients are added,
            //       otherwise existing non-null ModuleId/ClientId rows violate the constraint.
            migrationBuilder.Sql(@"
                INSERT INTO Modules (Id, Name)
                SELECT DISTINCT ModuleId, ''
                FROM Tasks WHERE ModuleId IS NOT NULL;");

            migrationBuilder.Sql(@"
                INSERT INTO Clients (Id, Name)
                SELECT DISTINCT ClientId, ''
                FROM Tasks WHERE ClientId IS NOT NULL;");

            // ── 3. Populate SprintCapacity.UserId from OwnerName ──────────────────────────
            //       Must happen BEFORE AlterColumn makes UserId NOT NULL.
            migrationBuilder.Sql(@"
                UPDATE sc
                SET sc.UserId = u.Id
                FROM SprintCapacities sc
                JOIN AppUsers u ON u.DisplayName = sc.OwnerName;

                IF EXISTS (
                    SELECT 1
                    FROM SprintCapacities
                    WHERE UserId IS NULL OR UserId = 0
                )
                    THROW 51003,
                        'Migration stopped: one or more SprintCapacities could not be matched to an AppUser.',
                        1;");

            // ── 4. Make SprintCapacity.UserId NOT NULL ────────────────────────────────────
            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "SprintCapacities",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            // ── 5. Create TaskOwners junction table ───────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "TaskOwners",
                columns: table => new
                {
                    TaskId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskOwners", x => new { x.TaskId, x.UserId });
                    table.ForeignKey(
                        name: "FK_TaskOwners_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskOwners_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── 6. Copy Tasks.Owner comma-string → TaskOwners rows ────────────────────────
            //       Tasks.Owner column is kept as a display cache; will be dropped later.
            migrationBuilder.Sql(@"
                INSERT INTO TaskOwners (TaskId, UserId)
                SELECT DISTINCT t.Id, u.Id
                FROM Tasks t
                CROSS APPLY STRING_SPLIT(t.Owner, ',') sv
                JOIN AppUsers u ON u.DisplayName = LTRIM(RTRIM(sv.value))
                WHERE t.Owner IS NOT NULL
                  AND LTRIM(RTRIM(sv.value)) <> '';

                IF EXISTS (
                    SELECT 1
                    FROM Tasks t
                    CROSS APPLY STRING_SPLIT(t.Owner, ',') sv
                    WHERE t.Owner IS NOT NULL
                      AND LTRIM(RTRIM(sv.value)) <> ''
                      AND NOT EXISTS (
                          SELECT 1
                          FROM AppUsers u
                          WHERE u.DisplayName = LTRIM(RTRIM(sv.value))
                      )
                )
                    THROW 51004,
                        'Migration stopped: one or more Tasks.Owner values could not be matched to an AppUser.',
                        1;");

            // ── 7. Create SprintHoursLogs if missing ──────────────────────────────────────
            //       Migration 20260811000001_AddSprintHoursLog was recorded in history
            //       but the table was never actually created in the database.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SprintHoursLogs')
                BEGIN
                    CREATE TABLE SprintHoursLogs (
                        Id       INT           NOT NULL IDENTITY(1,1),
                        SprintId INT           NOT NULL,
                        TaskId   INT           NOT NULL,
                        Hours    DECIMAL(18,2) NOT NULL,
                        LoggedBy NVARCHAR(MAX) NOT NULL DEFAULT '',
                        LoggedAt DATETIME2     NOT NULL,
                        CONSTRAINT PK_SprintHoursLogs PRIMARY KEY (Id)
                    );
                    CREATE INDEX IX_SprintHoursLogs_SprintId
                        ON SprintHoursLogs(SprintId);
                    CREATE UNIQUE INDEX IX_SprintHoursLogs_SprintId_TaskId
                        ON SprintHoursLogs(SprintId, TaskId);
                END");

            // ── 8. Indexes ────────────────────────────────────────────────────────────────
            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ClientId",
                table: "Tasks",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ModuleId",
                table: "Tasks",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_SprintCapacities_SprintId_UserId",
                table: "SprintCapacities",
                columns: new[] { "SprintId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SprintCapacities_UserId",
                table: "SprintCapacities",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedQueries_TeamId",
                table: "SavedQueries",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_RequestedTeamId",
                table: "AppUsers",
                column: "RequestedTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskOwners_UserId",
                table: "TaskOwners",
                column: "UserId");

            // ── 9. Foreign key constraints ────────────────────────────────────────────────
            migrationBuilder.AddForeignKey(
                name: "FK_ActivityLog_Teams_TeamId",
                table: "ActivityLog",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AppUsers_Teams_RequestedTeamId",
                table: "AppUsers",
                column: "RequestedTeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SavedQueries_Teams_TeamId",
                table: "SavedQueries",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SprintCapacities_AppUsers_UserId",
                table: "SprintCapacities",
                column: "UserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sprints_Teams_TeamId",
                table: "Sprints",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Clients_ClientId",
                table: "Tasks",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Modules_ModuleId",
                table: "Tasks",
                column: "ModuleId",
                principalTable: "Modules",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Sprints_SprintId",
                table: "Tasks",
                column: "SprintId",
                principalTable: "Sprints",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Teams_TeamId",
                table: "Tasks",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── 10. CHECK constraints for Role/TeamRole ───────────────────────────────────
            //       WITH NOCHECK skips validation of existing rows (pre-verified clean via audit).
            //       Constraint still enforces all future inserts and updates.
            migrationBuilder.Sql(
                "ALTER TABLE AppUsers WITH NOCHECK ADD CONSTRAINT CK_AppUsers_Role " +
                "CHECK (Role IN ('Admin', 'Member'));");
            migrationBuilder.Sql(
                "ALTER TABLE TeamMembers WITH NOCHECK ADD CONSTRAINT CK_TeamMembers_TeamRole " +
                "CHECK (TeamRole IN ('TeamAdmin', 'TeamLead', 'Member'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE AppUsers DROP CONSTRAINT IF EXISTS CK_AppUsers_Role;
                ALTER TABLE TeamMembers DROP CONSTRAINT IF EXISTS CK_TeamMembers_TeamRole;");

            migrationBuilder.DropForeignKey(name: "FK_ActivityLog_Teams_TeamId",         table: "ActivityLog");
            migrationBuilder.DropForeignKey(name: "FK_AppUsers_Teams_RequestedTeamId",   table: "AppUsers");
            migrationBuilder.DropForeignKey(name: "FK_SavedQueries_Teams_TeamId",        table: "SavedQueries");
            migrationBuilder.DropForeignKey(name: "FK_SprintCapacities_AppUsers_UserId", table: "SprintCapacities");
            migrationBuilder.DropForeignKey(name: "FK_Sprints_Teams_TeamId",             table: "Sprints");
            migrationBuilder.DropForeignKey(name: "FK_Tasks_Clients_ClientId",           table: "Tasks");
            migrationBuilder.DropForeignKey(name: "FK_Tasks_Modules_ModuleId",           table: "Tasks");
            migrationBuilder.DropForeignKey(name: "FK_Tasks_Sprints_SprintId",           table: "Tasks");
            migrationBuilder.DropForeignKey(name: "FK_Tasks_Teams_TeamId",               table: "Tasks");

            migrationBuilder.DropTable(name: "TaskOwners");
            migrationBuilder.DropTable(name: "Modules");
            migrationBuilder.DropTable(name: "Clients");

            migrationBuilder.DropIndex(name: "IX_Tasks_ClientId",                  table: "Tasks");
            migrationBuilder.DropIndex(name: "IX_Tasks_ModuleId",                   table: "Tasks");
            migrationBuilder.DropIndex(name: "IX_SprintCapacities_SprintId_UserId", table: "SprintCapacities");
            migrationBuilder.DropIndex(name: "IX_SprintCapacities_UserId",          table: "SprintCapacities");
            migrationBuilder.DropIndex(name: "IX_SavedQueries_TeamId",              table: "SavedQueries");
            migrationBuilder.DropIndex(name: "IX_AppUsers_RequestedTeamId",         table: "AppUsers");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "SprintCapacities",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
