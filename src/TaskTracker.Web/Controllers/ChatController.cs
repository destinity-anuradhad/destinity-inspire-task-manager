using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

[Authorize]
public class ChatController(IChatService chat, IAttachmentStorage storage) : AppControllerBase
{
    private const long MaxFileBytes = 10 * 1024 * 1024;   // 10 MB per file
    private const int  MaxFiles     = 10;

    public IActionResult Index()
    {
        if (!ActiveTeamId().HasValue) return RedirectToAction("AutoSelect", "Teams");
        ViewData["ActiveNav"] = "Chat";
        ViewData["FullBleed"] = true;   // chat fills the viewport (no page padding / footer)
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Messages(int? before)
    {
        var teamId = ActiveTeamId();
        if (teamId == null) return BadRequest();
        return Json(await chat.GetMessagesAsync(teamId.Value, before, 30, CurrentUserId()));
    }

    [HttpGet]
    public async Task<IActionResult> Members()
    {
        var teamId = ActiveTeamId();
        if (teamId == null) return Json(Array.Empty<string>());
        return Json(await chat.GetMemberNamesAsync(teamId.Value));
    }

    [HttpGet]
    public async Task<IActionResult> Attachments()
    {
        var teamId = ActiveTeamId();
        if (teamId == null) return Json(Array.Empty<object>());
        return Json(await chat.GetTeamAttachmentsAsync(teamId.Value));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(60 * 1024 * 1024)]
    public async Task<IActionResult> Send(string? body, List<IFormFile>? files)
    {
        var teamId = ActiveTeamId();
        if (teamId == null) return Json(new { success = false, error = "No active team." });

        files ??= [];
        var hasText = !string.IsNullOrWhiteSpace(body);
        if (!hasText && files.Count == 0) return Json(new { success = false, error = "Type a message or attach a file." });
        if (files.Count > MaxFiles)       return Json(new { success = false, error = $"Up to {MaxFiles} files per message." });

        // 1) Create the message row so attachments have a parent.
        var messageId = await chat.CreateMessageAsync(teamId.Value, CurrentUserId(), CurrentDisplayName(), body);

        // 2) Validate + store each file blob (outside the web root), collect metadata.
        var metas = new List<ChatIncomingAttachment>();
        foreach (var file in files)
        {
            if (file.Length == 0) continue;
            if (file.Length > MaxFileBytes)
                return Json(new { success = false, error = $"'{file.FileName}' exceeds the 10 MB limit." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!FileSignatureValidator.IsAllowedExtension(ext))
                return Json(new { success = false, error = $"'{file.FileName}': file type not allowed." });

            await using (var head = file.OpenReadStream())
            {
                var header = new byte[FileSignatureValidator.HeaderLength];
                var read = await ReadFullyAsync(head, header);
                if (!FileSignatureValidator.ContentMatches(ext, header.AsSpan(0, read)))
                    return Json(new { success = false, error = $"'{file.FileName}': content does not match its type." });
            }

            var storedName = Guid.NewGuid().ToString("N") + ext;
            await using (var toStore = file.OpenReadStream())
                await storage.SaveAsync(teamId.Value, storedName, toStore);

            var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
            metas.Add(new ChatIncomingAttachment(file.FileName, storedName, file.Length, contentType));
        }

        // 3) Persist attachment records + broadcast the complete message to the team group.
        var dto = await chat.FinalizeAndBroadcastAsync(messageId, teamId.Value, metas);
        return Json(new { success = true, message = dto });
    }

    [HttpGet]
    public async Task<IActionResult> File(int id)
    {
        var teamId = ActiveTeamId();
        if (teamId == null) return NotFound();
        var f = await chat.GetAttachmentAsync(id, teamId.Value);
        if (f == null) return NotFound();

        var path = storage.ResolveExistingPath(f.TeamId, f.StoredName);
        if (path == null) return NotFound();

        // Only well-known raster images render inline; everything else downloads (avoids SVG/HTML XSS).
        var safeInline = f.ContentType is "image/png" or "image/jpeg" or "image/gif" or "image/webp";
        return safeInline ? PhysicalFile(path, f.ContentType!) : PhysicalFile(path, f.ContentType, f.FileName);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var teamId = ActiveTeamId();
        if (teamId == null) return Json(new { success = false });
        var isTeamAdmin = IsAdmin() || CurrentTeamRole() == "TeamAdmin";
        var (ok, error) = await chat.DeleteMessageAsync(id, teamId.Value, CurrentUserId(), isTeamAdmin);
        return Json(new { success = ok, error });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, string? body)
    {
        var teamId = ActiveTeamId();
        if (teamId == null) return Json(new { success = false, error = "No active team." });
        var (ok, error) = await chat.EditMessageAsync(id, teamId.Value, CurrentUserId(), body ?? "");
        return Json(new { success = ok, error });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> React(int id, string emoji)
    {
        var teamId = ActiveTeamId();
        if (teamId == null) return Json(new { success = false, error = "No active team." });
        var (ok, error) = await chat.ToggleReactionAsync(id, teamId.Value, CurrentUserId(), emoji ?? "");
        return Json(new { success = ok, error });
    }

    private static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total));
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
