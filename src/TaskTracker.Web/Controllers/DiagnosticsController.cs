using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;
using TaskTracker.Web.Auth;

namespace TaskTracker.Controllers;

/// <summary>
/// System-administrator diagnostics. Read-only checks used to verify data health before risky
/// migrations — currently the backlog #7 owner-string reconciliation.
/// </summary>
[Authorize(Policy = Policies.SystemAdmin)]
public class DiagnosticsController(OwnerReconciliationService reconciliation) : AppControllerBase
{
    /// <summary>
    /// Reports whether the TaskOwners junction agrees with the legacy Owner string for every task.
    /// A clean result (isClean = true) is the gate for retiring the string. Changes nothing.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> OwnerReconciliation()
    {
        var report = await reconciliation.ReconcileAsync();
        return Json(new
        {
            report.TasksChecked,
            report.Mismatches,
            report.TasksWithUnresolvableNames,
            report.IsClean,
            sampleRows = report.Rows.Take(500),   // cap the payload; counts above are exact
        });
    }
}
