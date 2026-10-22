using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;
using TaskTracker.Web.Auth;

namespace TaskTracker.Controllers;

/// <summary>
/// Interactive management for outbound webhook subscriptions and their delivery history. System
/// administrators manage every subscription; team administrators manage those scoped to their team.
/// </summary>
[Authorize(Policy = Policies.TeamAdmin)]
public class WebhooksController(IWebhookService webhooks, IActivityService activity) : AppControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var subscriptions = await webhooks.ListAsync(ActiveTeamId(), IsSysAdmin());
        ViewBag.Deliveries      = await webhooks.GetRecentDeliveriesAsync(ActiveTeamId(), IsSysAdmin(), 50);
        ViewBag.AvailableEvents = WebhookEvents.All;
        ViewBag.IsSysAdmin      = IsSysAdmin();
        ViewBag.ActiveTeam      = ActiveTeamName();
        ViewData["ActiveNav"]   = "Webhooks";
        return View(subscriptions);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string url, string[]? events)
    {
        var teamId = ActiveTeamId();
        if (teamId is null)
        {
            TempData["Error"] = "Select a team before creating a webhook.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var request = new WebhookCreateRequest(
                name ?? "", url ?? "", teamId.Value, CurrentUserId(),
                WebhookEvents.Parse(WebhookEvents.Normalize(events ?? [])), CurrentDisplayName());
            var result = await webhooks.CreateAsync(request);

            await activity.LogAsync(CurrentDisplayName(), "Webhook Created", "System",
                $"'{result.Subscription.Name}' → {result.Subscription.Url} [{result.Subscription.EventTypes}]",
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.Request.Headers.UserAgent.ToString());

            TempData["NewWebhookSecret"] = result.Secret;
            TempData["NewWebhookName"]   = result.Subscription.Name;
        }
        catch (ArgumentException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id, bool active)
    {
        var ok = await webhooks.SetActiveAsync(id, active, ActiveTeamId(), IsSysAdmin());
        TempData[ok ? "Success" : "Error"] = ok ? (active ? "Webhook enabled." : "Webhook paused.") : "Webhook not found.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var ok = await webhooks.DeleteAsync(id, ActiveTeamId(), IsSysAdmin());
        if (ok)
            await activity.LogAsync(CurrentDisplayName(), "Webhook Deleted", "System", $"Subscription #{id} deleted",
                HttpContext.Connection.RemoteIpAddress?.ToString(), HttpContext.Request.Headers.UserAgent.ToString());
        TempData[ok ? "Success" : "Error"] = ok ? "Webhook deleted." : "Webhook not found.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Replay(long deliveryId)
    {
        var ok = await webhooks.ReplayAsync(deliveryId, ActiveTeamId(), IsSysAdmin());
        TempData[ok ? "Success" : "Error"] = ok ? "Delivery re-queued." : "Delivery could not be re-queued.";
        return RedirectToAction(nameof(Index));
    }
}
