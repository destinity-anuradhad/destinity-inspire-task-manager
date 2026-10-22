using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;
using TaskTracker.Web.Auth;

namespace TaskTracker.Controllers;

/// <summary>
/// Interactive management surface for integration API keys. System administrators manage every
/// key; team administrators manage the keys scoped to their active team. Keys act as the creating
/// user, scoped to the active team, limited to the granted scopes.
/// </summary>
[Authorize(Policy = Policies.TeamAdmin)]
public class ApiClientsController(IApiClientService apiClients, IActivityService activity) : AppControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var clients = await apiClients.ListAsync(ActiveTeamId(), IsSysAdmin());
        ViewBag.IsSysAdmin    = IsSysAdmin();
        ViewBag.ActiveTeam    = ActiveTeamName();
        ViewBag.AvailableScopes = ApiScopes.All;
        ViewData["ActiveNav"] = "ApiKeys";
        return View(clients);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string[]? scopes, int expiresInDays, bool ownTasksOnly = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "A name for the key is required.";
            return RedirectToAction(nameof(Index));
        }

        var teamId = ActiveTeamId();
        if (teamId is null && !IsSysAdmin())
        {
            TempData["Error"] = "Select a team before creating an API key.";
            return RedirectToAction(nameof(Index));
        }

        var normalized = ApiScopes.Normalize(scopes ?? []);
        if (normalized.Length == 0)
        {
            TempData["Error"] = "Select at least one scope for the key.";
            return RedirectToAction(nameof(Index));
        }

        DateTime? expiresAt = expiresInDays > 0 ? DateTime.UtcNow.AddDays(expiresInDays) : null;

        var request = new ApiClientCreateRequest(
            name.Trim(), CurrentUserId(), teamId, ApiScopes.Parse(normalized), expiresAt, ownTasksOnly);
        var result = await apiClients.CreateAsync(request);

        await activity.LogAsync(CurrentDisplayName(), "API Key Created", "System",
            $"Key '{result.Client.Name}' ({result.Client.KeyPrefix}), scopes: {result.Client.Scopes}" + (ownTasksOnly ? ", own tasks only" : ""),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            HttpContext.Request.Headers.UserAgent.ToString());

        // Surface the plaintext exactly once — it can never be recovered afterwards.
        TempData["NewApiKey"]     = result.PlaintextKey;
        TempData["NewApiKeyName"] = result.Client.Name;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(int id)
    {
        var revoked = await apiClients.RevokeAsync(id, ActiveTeamId(), IsSysAdmin());
        if (!revoked)
        {
            TempData["Error"] = "That key could not be revoked.";
            return RedirectToAction(nameof(Index));
        }

        await activity.LogAsync(CurrentDisplayName(), "API Key Revoked", "System",
            $"Key #{id} revoked",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            HttpContext.Request.Headers.UserAgent.ToString());

        TempData["Success"] = "API key revoked.";
        return RedirectToAction(nameof(Index));
    }
}
