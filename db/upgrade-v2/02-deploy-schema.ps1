<#
  02-deploy-schema.ps1
  Schema-compare deploy of reference.dacpac (current app schema) onto the restored _v2.
  Adds ONLY the missing tables/columns (additive, non-destructive). Your v1 data is untouched.

  DRY RUN (default):  writes deploy-report.xml + deploy-preview.sql (review; nothing applied)
  APPLY:              pass -Apply to actually publish the schema

  Examples:
    pwsh ./02-deploy-schema.ps1 -Password <password>            # dry run — review the preview
    pwsh ./02-deploy-schema.ps1 -Password <password> -Apply     # apply
#>
param(
  [string]$Server   = "10.4.1.14",
  [string]$Database = "DestiniyInspire_TaskManager_v2",
  [string]$User     = "stpluser",
  [Parameter(Mandatory=$true)][string]$Password,
  [switch]$Apply
)
$ErrorActionPreference = "Stop"
$sp     = "$env:USERPROFILE\.dotnet\tools\sqlpackage.exe"
$dacpac = Join-Path $PSScriptRoot "reference.dacpac"
$conn   = "Server=$Server;Database=$Database;User Id=$User;Password=$Password;TrustServerCertificate=True;Encrypt=False;"

# Safety: additive only. Never drop what v1 has; halt if any change could lose data.
$props = @(
  "/p:BlockOnPossibleDataLoss=true"
  "/p:GenerateSmartDefaults=true"
  "/p:DropObjectsNotInSource=false"
  "/p:IgnoreColumnOrder=true"
  "/p:IgnorePermissions=true"
  "/p:IgnoreRoleMembership=true"
  "/p:IgnoreUserSettingsObjects=true"
  "/p:IncludeTransactionalScripts=true"
)

if (-not (Test-Path $dacpac)) { throw "reference.dacpac not found next to this script." }

if ($Apply) {
  Write-Host "APPLYING schema to [$Database] on [$Server] ..." -ForegroundColor Yellow
  & $sp /Action:Publish /SourceFile:$dacpac /TargetConnectionString:$conn @props
  Write-Host "Publish complete. Next: run 03-stamp-history.sql, then 04-postmigrate_v2.sql." -ForegroundColor Green
}
else {
  $report  = Join-Path $PSScriptRoot "deploy-report.xml"
  $preview = Join-Path $PSScriptRoot "deploy-preview.sql"
  Write-Host "DRY RUN (no -Apply): generating deploy report + script for [$Database] ..." -ForegroundColor Cyan
  & $sp /Action:DeployReport /SourceFile:$dacpac /TargetConnectionString:$conn @props /OutputPath:$report
  & $sp /Action:Script       /SourceFile:$dacpac /TargetConnectionString:$conn @props /OutputPath:$preview
  Write-Host "Review these, then re-run with -Apply:" -ForegroundColor Green
  Write-Host "  $report"
  Write-Host "  $preview"
}
