/* Baseline a schema created by `dotnet ef dbcontext script`.
   Run only against DestiniyInspire_TaskManager_v2 after schema validation. */
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
CREATE TABLE dbo.__EFMigrationsHistory (MigrationId nvarchar(150) NOT NULL PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL);
INSERT dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
SELECT v.MigrationId, N'10.0.9' FROM (VALUES
(N'20260630094913_InitialCreate'),(N'20260630094959_AddUserEmailNotifications'),(N'20260630110000_AddLabelsAndAcceptanceCriteria'),
(N'20260701120000_AddTaskAttachments'),(N'20260701130000_AddWipLimitToWorkStates'),(N'20260701130001_AddSwimlaneToTasks'),
(N'20260810072319_AddItemTypesToWorkStates'),(N'20260810110637_AddActualHoursToTasks'),(N'20260811000001_AddSprintHoursLog'),
(N'20260812115920_RemoveTaskOwners'),(N'20260817032043_NormalizationPhase1_TaskOwners'),(N'20260831000000_RepairTeamLeadConstraint'),
(N'20260831000100_BackfillVerifiedLegacyTaskOwners'),(N'20260901000000_AddUserSessionVersion'),(N'20260901042508_AddVersionedWorkflowModel'),
(N'20260901082341_AddImmutableSprintHistory'),(N'20260901083457_AddAppendOnlyTimeEntries'),(N'20260901095933_AddEstimateUnits'),
(N'20260901103000_AddUserTimeOff'),(N'20260901111927_AddDurableEmailOutbox'),(N'20260901113614_MakeProjectsFirstClass'),
(N'20260901121801_LinkTasksToGovernedProjects'),(N'20260901123003_AddProjectReleases'),(N'20260901123814_AddTaskChecklists'),
(N'20260901124918_AddTaskTemplates'),(N'20260901132434_AddRecurringTaskDefinitions'),(N'20260901133156_AddRecurringTaskRuns'),
(N'20260901133439_AddGovernedCustomFields')) v(MigrationId)
WHERE NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory h WHERE h.MigrationId=v.MigrationId);
COMMIT TRANSACTION;
