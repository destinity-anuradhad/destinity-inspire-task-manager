using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskTracker.Migrations;

public partial class RenameLeadToTeamLead : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'TeamMembers')
            BEGIN
                IF OBJECT_ID(N'CK_TeamMembers_TeamRole', N'C') IS NOT NULL
                    ALTER TABLE [TeamMembers] DROP CONSTRAINT [CK_TeamMembers_TeamRole];

                UPDATE [TeamMembers] SET [TeamRole] = 'TeamLead' WHERE [TeamRole] = 'Lead';

                ALTER TABLE [TeamMembers] WITH CHECK ADD CONSTRAINT [CK_TeamMembers_TeamRole]
                    CHECK ([TeamRole] IN ('TeamAdmin', 'TeamLead', 'Member'));
                ALTER TABLE [TeamMembers] CHECK CONSTRAINT [CK_TeamMembers_TeamRole];
            END
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'TeamMembers')
            BEGIN
                IF OBJECT_ID(N'CK_TeamMembers_TeamRole', N'C') IS NOT NULL
                    ALTER TABLE [TeamMembers] DROP CONSTRAINT [CK_TeamMembers_TeamRole];

                UPDATE [TeamMembers] SET [TeamRole] = 'Lead' WHERE [TeamRole] = 'TeamLead';

                ALTER TABLE [TeamMembers] WITH CHECK ADD CONSTRAINT [CK_TeamMembers_TeamRole]
                    CHECK ([TeamRole] IN ('TeamAdmin', 'Lead', 'Member'));
                ALTER TABLE [TeamMembers] CHECK CONSTRAINT [CK_TeamMembers_TeamRole];
            END
        ");
    }
}
