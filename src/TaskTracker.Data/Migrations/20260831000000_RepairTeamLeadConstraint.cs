using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TaskTracker.Data;

#nullable disable

namespace TaskTracker.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260831000000_RepairTeamLeadConstraint")]
public partial class RepairTeamLeadConstraint : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'[TeamMembers]', N'U') IS NOT NULL
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM sys.check_constraints
                    WHERE [parent_object_id] = OBJECT_ID(N'[TeamMembers]')
                      AND [name] = N'CK_TeamMembers_TeamRole'
                )
                    ALTER TABLE [TeamMembers] DROP CONSTRAINT [CK_TeamMembers_TeamRole];

                UPDATE [TeamMembers]
                SET [TeamRole] = N'TeamLead'
                WHERE [TeamRole] = N'Lead';

                IF EXISTS (
                    SELECT 1
                    FROM [TeamMembers]
                    WHERE [TeamRole] NOT IN (N'TeamAdmin', N'TeamLead', N'Member')
                )
                    THROW 51005,
                        'Migration stopped: TeamMembers contains an unsupported TeamRole.',
                        1;

                ALTER TABLE [TeamMembers] WITH CHECK
                    ADD CONSTRAINT [CK_TeamMembers_TeamRole]
                    CHECK ([TeamRole] IN (N'TeamAdmin', N'TeamLead', N'Member'));
                ALTER TABLE [TeamMembers]
                    CHECK CONSTRAINT [CK_TeamMembers_TeamRole];
            END
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'[TeamMembers]', N'U') IS NOT NULL
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM sys.check_constraints
                    WHERE [parent_object_id] = OBJECT_ID(N'[TeamMembers]')
                      AND [name] = N'CK_TeamMembers_TeamRole'
                )
                    ALTER TABLE [TeamMembers] DROP CONSTRAINT [CK_TeamMembers_TeamRole];

                UPDATE [TeamMembers]
                SET [TeamRole] = N'Lead'
                WHERE [TeamRole] = N'TeamLead';

                ALTER TABLE [TeamMembers] WITH CHECK
                    ADD CONSTRAINT [CK_TeamMembers_TeamRole]
                    CHECK ([TeamRole] IN (N'TeamAdmin', N'Lead', N'Member'));
                ALTER TABLE [TeamMembers]
                    CHECK CONSTRAINT [CK_TeamMembers_TeamRole];
            END
        ");
    }
}
