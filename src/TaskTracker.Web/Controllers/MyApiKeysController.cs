using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

/// <summary>
/// Self-service personal API keys (profile menu → My API keys). Any signed-in user can mint keys that act
/// as themselves in their active team, limited to the chosen scopes, always with an expiry, and by default
/// limited to their own tasks. Team-wide integration keys stay under Admin → API Keys.
/// </summary>
[Authorize]
public class MyApiKeysController(IApiClientService apiClients, IActivityService activity) : AppControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var keys = await apiClients.ListForOwnerAsync(CurrentUserId());
        ViewBag.ActiveTeam = ActiveTeamName();
        ViewBag.AvailableScopes = ApiScopes.All;
        ViewBag.MaxDays = PersonalKeyRules.MaxDays;
        ViewBag.MaxActiveKeys = PersonalKeyRules.MaxActiveKeys;
        return View(keys);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string[]? scopes, int expiresInDays, bool ownTasksOnly)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "A name for the key is required.";
            return RedirectToAction(nameof(Index));
        }

        if (ApiScopes.Normalize(scopes ?? []).Length == 0)
        {
            TempData["Error"] = "Select at least one scope for the key.";
            return RedirectToAction(nameof(Index));
        }

        ApiClientCreateResult result;
        try
        {
            result = await apiClients.CreatePersonalAsync(new PersonalKeyRequest(
                name.Trim(), CurrentUserId(), IsSysAdmin(), ActiveTeamId(), scopes ?? [], expiresInDays, ownTasksOnly));
        }
        catch (ArgumentException ex)
        {
            TempData["Error"] = ex.Message.Split(" (Parameter")[0];
            return RedirectToAction(nameof(Index));
        }

        await activity.LogAsync(CurrentDisplayName(), "Personal API Key Created", "System",
            $"Key '{result.Client.Name}' ({result.Client.KeyPrefix}), scopes: {result.Client.Scopes}" + (ownTasksOnly ? ", own tasks only" : ""),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            HttpContext.Request.Headers.UserAgent.ToString());

        // Surface the plaintext exactly once — it can never be recovered afterwards.
        TempData["NewApiKey"] = result.PlaintextKey;
        TempData["NewApiKeyName"] = result.Client.Name;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(int id)
    {
        if (!await apiClients.RevokeOwnAsync(id, CurrentUserId()))
        {
            TempData["Error"] = "That key could not be revoked.";
            return RedirectToAction(nameof(Index));
        }

        await activity.LogAsync(CurrentDisplayName(), "Personal API Key Revoked", "System",
            $"Key #{id} revoked by its owner",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            HttpContext.Request.Headers.UserAgent.ToString());

        TempData["Success"] = "API key revoked.";
        return RedirectToAction(nameof(Index));
    }
}
