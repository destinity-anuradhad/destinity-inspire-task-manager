using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;
using TaskTracker.Web.Auth;

namespace TaskTracker.Controllers;

/// <summary>
/// Interactive management surface for team automation rules ("WHEN … THEN …"). System administrators
/// manage every team's rules; team administrators manage the rules scoped to their active team. Rules
/// run as a best-effort side effect of task lifecycle events and are fully audited.
/// </summary>
[Authorize(Policy = Policies.TeamAdmin)]
public class AutomationsController(IAutomationService automations, IActivityService activity) : AppControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var rules = await automations.ListAsync(ActiveTeamId(), IsSysAdmin());
        ViewBag.IsSysAdmin  = IsSysAdmin();
        ViewBag.ActiveTeam  = ActiveTeamName();
        ViewBag.Triggers    = AutomationTriggers.Triggers;
        ViewBag.Fields      = AutomationTriggers.Fields;
        ViewBag.Operators   = AutomationTriggers.Operators;
        ViewBag.Actions     = AutomationTriggers.Actions;
        ViewBag.RecentRuns  = await automations.GetRecentRunsAsync(ActiveTeamId(), IsSysAdmin(), 25);
        ViewData["ActiveNav"] = "Automations";
        return View(rules);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        string name, string triggerType,
        string? conditionField, string? conditionOperator, string? conditionValue,
        string actionType, string? actionConfig)
    {
        var teamId = ActiveTeamId();
        if (teamId is null)
        {
            TempData["Error"] = "Select a team before creating an automation rule.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var input = new AutomationRuleInput(
                name ?? "", teamId.Value, triggerType ?? "",
                conditionField, conditionOperator, conditionValue,
                actionType ?? "", actionConfig, CurrentDisplayName());
            var rule = await automations.CreateAsync(input);

            await activity.LogAsync(CurrentDisplayName(), "Automation Rule Created", "System",
                $"Rule '{rule.Name}' — when {AutomationTriggers.Label(rule.TriggerType)} then {AutomationTriggers.Label(rule.ActionType)}",
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.Request.Headers.UserAgent.ToString());

            TempData["Success"] = "Automation rule created.";
        }
        catch (ArgumentException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(int ruleId, int taskId)
    {
        var result = await automations.PreviewAsync(ruleId, taskId, ActiveTeamId(), IsSysAdmin());
        if (!result.RuleFound || !result.TaskFound)
        {
            TempData["Error"] = result.Description;
        }
        else
        {
            var verdict = result.Matched ? "✓ would run" : "✗ would not run";
            TempData["Preview"] = $"Rule “{result.RuleName}” on task #{taskId} ({result.TaskGoal}): {verdict} — {result.Description}";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id, bool enabled)
    {
        var ok = await automations.SetEnabledAsync(id, enabled, ActiveTeamId(), IsSysAdmin());
        TempData[ok ? "Success" : "Error"] = ok
            ? (enabled ? "Rule enabled." : "Rule disabled.")
            : "That rule could not be updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var ok = await automations.DeleteAsync(id, ActiveTeamId(), IsSysAdmin());
        if (ok)
            await activity.LogAsync(CurrentDisplayName(), "Automation Rule Deleted", "System",
                $"Rule #{id} deleted",
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.Request.Headers.UserAgent.ToString());

        TempData[ok ? "Success" : "Error"] = ok ? "Automation rule deleted." : "That rule could not be deleted.";
        return RedirectToAction(nameof(Index));
    }
}
