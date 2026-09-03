using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

[Authorize]
public class TasksController(ITaskService tasks, ITeamService teams, IProjectService projects, ITaskTemplateService templates, IAttachmentStorage attachmentStorage, IConfiguration config) : AppControllerBase
{
    private bool LegacyFullTaskEditEnabled() =>
        config.GetValue<bool>("Features:AllowLegacyFullTaskEdit");

    private bool LegacyBulkTaskMutationsEnabled() =>
        config.GetValue<bool>("Features:AllowLegacyBulkTaskMutations");

    private IActionResult MutationTemporarilyDisabled(string operation) =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new {
            success = false,
            error = $"{operation} is temporarily disabled while data-integrity remediation is in progress."
        });

    public async Task<IActionResult> Index(
        string search = "", string project = "", string property = "",
        string? owner = null, string status = "", string priority = "",
        string dateFrom = "", string dateTo = "",
        int page = 1, int pageSize = 20,
        string sort = "", string sortDir = "desc",
        string view = "table", int calYear = 0, int calMonth = 0,
        string? tlfrom = null, string? tlto = null, string? tlmode = null)
    {
        var now = DateTime.Now;
        var filters = new TaskListFilters
        {
            Search   = search,  Project  = project,  Property = property,
            Owner    = owner,   Status   = status,   Priority = priority,
            DateFrom = dateFrom, DateTo  = dateTo,
            Page     = page,    PageSize = pageSize,
            Sort     = sort,    SortDir  = sortDir,  View = view,
            CalYear  = calYear  > 0 ? calYear  : now.Year,
            CalMonth = calMonth > 0 ? calMonth : now.Month,
            TlFrom   = tlfrom,  TlTo = tlto, TlMode = tlmode,
        };

        var result = await tasks.GetListAsync(filters, ActiveTeamId());
        var vm = result.ViewModel;
        ViewBag.AllSprints = result.AllSprints;
        ViewBag.GovernedProjects = await projects.GetForTeamAsync(ActiveTeamId());
        ViewBag.TaskTemplates = await templates.GetForTeamAsync(ActiveTeamId());

        if (view == "team" && result.TeamViewData != null)
        {
            var tvd = result.TeamViewData;
            ViewBag.TlFrom           = tvd.TlFrom;
            ViewBag.TlTo             = tvd.TlTo;
            ViewBag.TlMode           = tvd.TlMode;
            ViewBag.TlOwners         = tvd.TlOwners;
            ViewBag.TlProjectColors  = tvd.TlProjectColors;
            ViewBag.TlPropertyColors = tvd.TlPropertyColors;
            ViewBag.TlTasks          = tvd.TlTasks;
        }

        ViewData["ActiveNav"] = "Tasks";
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(TaskItem task)
    {
        if (task.Id != 0 && !LegacyFullTaskEditEnabled())
            return MutationTemporarilyDisabled("Full task editing");

        var forceClose = Request.Form["ForceClose"] == "true";
        var r = await tasks.SaveAsync(task, BuildUserCtx(), forceClose);
        if (!r.Success)
        {
            if (r.OpenChildren.HasValue)
                return Json(new { success = false, openChildren = r.OpenChildren, error = r.Error });
            return Json(new { success = false, conflict = r.Conflict, error = r.Error });
        }
        return Json(new { success = true, capacityWarning = r.CapacityWarning, taskId = r.TaskId });
    }

    [HttpGet]
    public async Task<IActionResult> GetTask(int id)
    {
        var task = await tasks.GetByIdAsync(id, ActiveTeamId());
        if (task == null) return NotFound();
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        return Json(task, opts);
    }

    [HttpGet]
    public async Task<IActionResult> GetTemplate(int id)
    {
        var template = await templates.GetByIdAsync(id, ActiveTeamId());
        if (template == null) return NotFound();
        var checklist = await templates.GetChecklistAsync(id, ActiveTeamId());
        return Json(new { template, checklist });
    }

    [HttpGet]
    public async Task<IActionResult> GetComments(int taskId)
    {
        var result = await tasks.GetCommentsAsync(taskId, ActiveTeamId());
        if (result == null) return NotFound();
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetChecklist(int taskId)
    {
        var result = await tasks.GetChecklistAsync(taskId, ActiveTeamId());
        return result == null ? NotFound() : Json(result);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddChecklistItem(int taskId, string text)
    {
        var (ok, error) = await tasks.AddChecklistItemAsync(taskId, text, BuildUserCtx());
        return Json(new { success = ok, error });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleChecklistItem(int id, bool isCompleted)
    {
        var (ok, error) = await tasks.ToggleChecklistItemAsync(id, isCompleted, BuildUserCtx());
        return Json(new { success = ok, error });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteChecklistItem(int id)
    {
        var (ok, error) = await tasks.DeleteChecklistItemAsync(id, BuildUserCtx());
        return Json(new { success = ok, error });
    }

    [HttpGet]
    public async Task<IActionResult> GetHistory(int taskId)
    {
        var result = await tasks.GetHistoryAsync(taskId, ActiveTeamId());
        if (result == null) return NotFound();
        return Json(result);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddComment(int taskId, string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return Json(new { success = false, error = "Comment cannot be empty." });
        var (ok, error) = await tasks.AddCommentAsync(taskId, comment, BuildUserCtx());
        if (!ok) return Json(new { success = false, error });
        return Json(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> GetModules()
    {
        var himCs = config.GetConnectionString("HimConnection");
        return Json(await tasks.GetModulesAsync(himCs));
    }

    [HttpGet]
    public async Task<IActionResult> GetBacklog(string sprintFilter = "all")
    {
        var r = await tasks.GetBacklogAsync(sprintFilter, ActiveTeamId());
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        return Json(new { tasks = r.Tasks, sprints = r.Sprints }, opts);
    }

    [HttpGet]
    public async Task<IActionResult> GetBurndown(int sprintId)
    {
        var r = await tasks.GetBurndownAsync(sprintId, ActiveTeamId());
        if (r == null) return NotFound();
        return Json(new { sprintName = r.SprintName, total = r.Total, totalWeight = r.TotalWeight, days = r.Days, ideal = r.Ideal, weightDays = r.WeightDays, weightIdeal = r.WeightIdeal, historicalSnapshot = r.IsHistoricalSnapshot, carriedForward = r.CarriedForwardCount });
    }

    [HttpGet]
    public async Task<IActionResult> GetSprints()
        => Json(await tasks.GetSprintsAsync(ActiveTeamId()));

    [HttpGet]
    public async Task<IActionResult> GetParentItems(string itemType = "")
        => Json(await tasks.GetParentItemsAsync(itemType, ActiveTeamId()));

    [HttpGet]
    public async Task<IActionResult> SearchTasks(string q = "")
        => Json(await tasks.SearchAsync(q, ActiveTeamId()));

    [HttpGet]
    public async Task<IActionResult> GetLinks(int taskId)
    {
        var result = await tasks.GetLinksAsync(taskId, ActiveTeamId());
        if (result == null) return NotFound();
        return Json(result);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddLink(int fromTaskId, int toTaskId, string linkType)
    {
        var (ok, error) = await tasks.AddLinkAsync(fromTaskId, toTaskId, linkType, BuildUserCtx());
        if (!ok) return ok ? Ok() : BadRequest(new { message = error });
        return Ok();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveLink(int id)
    {
        var (ok, error) = await tasks.RemoveLinkAsync(id, BuildUserCtx());
        if (!ok) return error == "Not found" ? NotFound() : BadRequest(new { message = error });
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> GetClients(int moduleId = 0)
    {
        var himCs = config.GetConnectionString("HimConnection");
        // Re-validate role from DB — cookie claims can be stale after a role change.
        bool privileged = IsAdmin();
        if (!privileged && ActiveTeamId().HasValue)
        {
            var dbRole = await teams.GetCallerTeamRoleAsync(ActiveTeamId()!.Value, CurrentUserId());
            privileged = dbRole == "TeamAdmin";
        }
        return Json(await tasks.GetClientsAsync(moduleId, himCs, privileged));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, string status, string? swimlane = null, decimal? actualHours = null)
    {
        var forceClose = Request.Form["ForceClose"] == "true";
        var r = await tasks.UpdateStatusAsync(id, status, swimlane, BuildUserCtx(), forceClose, actualHours);
        if (!r.Success)
        {
            if (r.NeedOwner)
                return Json(new { success = false, needOwner = true, error = r.Error });
            if (r.NeedActualHours)
                return Json(new { success = false, needActualHours = true, error = r.Error });
            if (r.OpenChildren.HasValue)
                return Json(new { success = false, openChildren = r.OpenChildren, error = r.Error });
            return Json(new { success = false, error = r.Error });
        }
        return Json(new { success = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignSprint(int taskId, int? sprintId)
    {
        if (!LegacyBulkTaskMutationsEnabled())
            return MutationTemporarilyDisabled("Sprint assignment");

        var r = await tasks.AssignSprintAsync(taskId, sprintId, BuildUserCtx());
        if (!r.Success) return Json(new { success = false, error = r.Error });
        return Json(new { success = true, sprintId = r.SprintId, capacityWarning = r.CapacityWarning });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> LogTime(int taskId, decimal hours, DateTime workDate, string? note = null,
        decimal? remainingWorkHours = null)
    {
        var result = await tasks.LogTimeAsync(taskId, hours, workDate, note, remainingWorkHours, BuildUserCtx());
        if (!result.Success) return BadRequest(new { success = false, error = result.Error });
        return Json(new {
            success = true, actualHours = result.ActualHours, remainingWorkHours = result.RemainingWorkHours
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CorrectTimeEntry(long entryId, string? note = null)
    {
        var result = await tasks.CorrectTimeEntryAsync(entryId, note, BuildUserCtx());
        if (!result.Success) return BadRequest(new { success = false, error = result.Error });
        return Json(new { success = true, actualHours = result.ActualHours });
    }

    [HttpGet]
    public async Task<IActionResult> GetWatchers(int taskId)
    {
        var r = await tasks.GetWatchersAsync(taskId, ActiveTeamId(), User.FindFirst("DisplayName")?.Value ?? "");
        if (r == null) return NotFound();
        return Json(new { watchers = r.Watchers, watching = r.Watching, count = r.Count });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleWatch(int taskId)
    {
        var r = await tasks.ToggleWatchAsync(taskId, CurrentUsername(), ActiveTeamId());
        if (!r.Success) return Json(new { success = false, error = r.Error });
        return Json(new { success = true, watching = r.Watching, count = r.Count });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickSetOwner(int id, string owner)
    {
        var (ok, error) = await tasks.QuickSetOwnerAsync(id, owner, BuildUserCtx());
        if (!ok) return Json(new { success = false, error });
        return Json(new { success = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ClaimTask(int id)
    {
        var (ok, error) = await tasks.ClaimTaskAsync(id, BuildUserCtx());
        if (!ok) return Json(new { success = false, error });
        return Json(new { success = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReorderBacklog([FromBody] int[] ids)
    {
        if (ids == null || ids.Length == 0) return Ok();
        await tasks.ReorderBacklogAsync(ids, ActiveTeamId());
        return Ok();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var (ok, error) = await tasks.DeleteAsync(id, BuildUserCtx());
        if (!ok && error == "Forbidden")
        {
            TempData["Error"] = "You don't have permission to delete this task.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> RecycleBin()
    {
        // Re-validate against DB — cookie TeamRole can be stale.
        bool isPrivileged = IsAdmin();
        if (!isPrivileged && ActiveTeamId().HasValue)
        {
            var dbRole = await teams.GetCallerTeamRoleAsync(ActiveTeamId()!.Value, CurrentUserId());
            isPrivileged = dbRole == "TeamAdmin";
        }
        if (!isPrivileged) return Forbid();
        var taskList = await tasks.GetRecycleBinAsync(ActiveTeamId());
        ViewData["ActiveNav"] = "Recycle";
        return View(taskList);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> RestoreTask(int id)
    {
        var (ok, error) = await tasks.RestoreAsync(id);
        if (!ok) return Json(new { success = false, error });
        return Json(new { success = true });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> PermanentDelete(int id)
    {
        var (ok, error) = await tasks.PermanentDeleteAsync(id);
        if (!ok) return Json(new { success = false, error });
        return Json(new { success = true });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> EmptyBin()
    {
        var (ok, count) = await tasks.EmptyBinAsync(ActiveTeamId());
        return Json(new { success = ok, count });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDelete([FromBody] int[] ids)
    {
        if (!LegacyBulkTaskMutationsEnabled())
            return MutationTemporarilyDisabled("Bulk task changes");

        if (ids == null || ids.Length == 0) return Json(new { success = false, error = "No tasks selected" });
        var r = await tasks.BulkDeleteAsync(ids, BuildUserCtx());
        return Json(new { success = r.Success, count = r.Count, denied = r.Denied });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkChangeStatus([FromBody] BulkStatusRequest req)
    {
        if (!LegacyBulkTaskMutationsEnabled())
            return MutationTemporarilyDisabled("Bulk task changes");

        if (req?.Ids == null || req.Ids.Length == 0 || string.IsNullOrEmpty(req.Status))
            return Json(new { success = false, error = "Invalid request" });
        var r = await tasks.BulkChangeStatusAsync(req.Ids, req.Status, BuildUserCtx());
        return Json(new {
            success = r.Success, count = r.Count, denied = r.Denied, noOwner = r.NoOwner,
            invalidTransition = r.InvalidTransition, needActualHours = r.NeedActualHours,
            openChildren = r.OpenChildren
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkChangeOwner([FromBody] BulkOwnerRequest req)
    {
        if (!LegacyBulkTaskMutationsEnabled())
            return MutationTemporarilyDisabled("Bulk task changes");

        if (req?.Ids == null || req.Ids.Length == 0)
            return Json(new { success = false, error = "Invalid request" });
        var r = await tasks.BulkChangeOwnerAsync(req.Ids, req.Owner, BuildUserCtx());
        return Json(new { success = r.Success, count = r.Count, denied = r.Denied });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkAssignSprint([FromBody] BulkSprintRequest req)
    {
        if (!LegacyBulkTaskMutationsEnabled())
            return MutationTemporarilyDisabled("Bulk task changes");

        if (req?.Ids == null || req.Ids.Length == 0)
            return Json(new { success = false, error = "Invalid request" });
        var r = await tasks.BulkAssignSprintAsync(req.Ids, req.SprintId, BuildUserCtx());
        return Json(new { success = r.Success, count = r.Count, skipped = r.Skipped, denied = r.Denied });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkChangePriority([FromBody] BulkPriorityRequest req)
    {
        if (!LegacyBulkTaskMutationsEnabled())
            return MutationTemporarilyDisabled("Bulk task changes");

        if (req?.Ids == null || req.Ids.Length == 0)
            return Json(new { success = false, error = "Invalid request" });
        var r = await tasks.BulkChangePriorityAsync(req.Ids, req.Priority, BuildUserCtx());
        return Json(new { success = r.Success, count = r.Count, denied = r.Denied });
    }

    [HttpGet]
    public async Task<IActionResult> Export(
        string search = "", string project = "", string property = "",
        string? owner = null, string status = "", string priority = "",
        string dateFrom = "", string dateTo = "")
    {
        var f = new ExportFilters
        {
            Search = search, Project = project, Property = property,
            Owner = owner, Status = status, Priority = priority,
            DateFrom = dateFrom, DateTo = dateTo,
        };
        var (bytes, fileName) = await tasks.ExportCsvAsync(f, ActiveTeamId());
        return File(bytes, "text/csv", fileName);
    }

    [HttpGet]
    public async Task<IActionResult> GetLabels()
        => Json(await tasks.GetLabelsAsync(ActiveTeamId()));

    [HttpGet]
    public async Task<IActionResult> GetChildStats(int id)
    {
        var r = await tasks.GetChildStatsAsync(id, ActiveTeamId());
        if (r == null) return NotFound();
        return Json(new { total = r.Total, done = r.Done, pct = r.Pct, leafType = r.LeafType });
    }

    // ── Attachments (file I/O stays in controller) ────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAttachments(int taskId)
    {
        var task = await tasks.GetByIdAsync(taskId, ActiveTeamId());
        if (task == null) return NotFound();
        return Json(await tasks.GetAttachmentsAsync(taskId));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadAttachment(int taskId, IFormFile file)
    {
        var task = await tasks.GetByIdAsync(taskId, ActiveTeamId());
        if (task == null) return Json(new { success = false, error = "Task not found." });

        if (file == null || file.Length == 0)
            return Json(new { success = false, error = "No file selected." });
        if (file.Length > 10 * 1024 * 1024)
            return Json(new { success = false, error = "File exceeds 10 MB limit." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!FileSignatureValidator.IsAllowedExtension(ext))
            return Json(new { success = false, error = "File type not allowed." });

        // Verify the file's real content matches its extension — a renamed executable/script is rejected
        // here even though its extension is on the allow-list.
        await using var upload = file.OpenReadStream();
        var header = new byte[FileSignatureValidator.HeaderLength];
        var read = await ReadFullyAsync(upload, header);
        if (!FileSignatureValidator.ContentMatches(ext, header.AsSpan(0, read)))
            return Json(new { success = false, error = "File content does not match its type." });

        var storedName = Guid.NewGuid().ToString("N") + ext;
        await using (var toStore = file.OpenReadStream())   // fresh stream from the start of the upload
            await attachmentStorage.SaveAsync(taskId, storedName, toStore);

        var displayName = User.FindFirst("DisplayName")?.Value ?? "";
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        var (ok, error) = await tasks.SaveAttachmentRecordAsync(taskId, file.FileName, storedName, file.Length, contentType, displayName);
        if (!ok)
        {
            attachmentStorage.Delete(taskId, storedName);
            return Json(new { success = false, error });
        }
        return Json(new { success = true });
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

    [HttpGet]
    public async Task<IActionResult> DownloadAttachment(int id)
    {
        var att = await tasks.GetDownloadAsync(id, ActiveTeamId());
        if (att == null) return NotFound();

        var path = attachmentStorage.ResolveExistingPath(att.TaskId, att.StoredName);
        if (path == null) return NotFound();
        // Named file → Content-Disposition: attachment, so nothing renders inline in the browser.
        return PhysicalFile(path, att.ContentType, att.FileName);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAttachment(int id)
    {
        var displayName = User.FindFirst("DisplayName")?.Value ?? "";
        var result = await tasks.DeleteAttachmentAsync(id, displayName, IsAdmin());
        if (!result.Success) return Json(new { success = false, error = result.Error });

        if (!string.IsNullOrEmpty(result.StoredName))
            attachmentStorage.Delete(result.TaskId, result.StoredName);

        return Json(new { success = true });
    }

    // ── Demo seed ─────────────────────────────────────────────────────────────

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> SeedDemo([FromServices] AppDbContext db)
    {
        var teamId = ActiveTeamId();
        if (!teamId.HasValue)
            return Json(new { success = false, error = "No active team selected." });

        var memberIds = await db.TeamMembers
            .Where(m => m.TeamId == teamId)
            .Select(m => m.UserId)
            .ToListAsync();

        var members = await db.AppUsers
            .Where(u => memberIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToListAsync();

        if (!members.Any())
            return Json(new { success = false, error = "No members in this team. Add members first." });

        var rng     = new Random();
        var creator = User.FindFirst("DisplayName")?.Value ?? "Admin";
        var now     = DateTime.Now;

        // (goal, type, priority, status, module, client, daysStart, daysEnd, weight)
        var seeds = new (string Goal, string Type, string Priority, string Status, string Module, string Client, int DsS, int DsE, int Wt)[]
        {
            ("Night audit fails when multiple rate plans are active",              "Bug",     "Critical", "Active",      "Billing & Payments",  "Grand Colombo Hotel",   -3,   5,  3),
            ("Reservation confirmation email not sent after OTA booking",         "Bug",     "High",     "In Progress", "Reservations",         "Kandy City Resort",     -5,   3,  2),
            ("Room status not syncing after housekeeping checkout scan",          "Bug",     "High",     "New",         "Housekeeping",         "Galle Face Suites",      0,   7,  4),
            ("Invoice PDF corrupts guest name with special characters",           "Bug",     "High",     "To Do",       "Billing & Payments",  "Bentota Beach Club",    -1,   4,  2),
            ("Group check-in fails when folio has more than 10 rooms",           "Bug",     "Critical", "In Progress", "Front Office",         "Grand Colombo Hotel",   -7,   2,  5),
            ("OTA channel manager showing incorrect room availability",           "Bug",     "Critical", "New",         "Reservations",         "Nuwara Eliya Heights",  -2,   6,  4),
            ("City ledger balance not reconciling with AR aging report",          "Bug",     "High",     "To Do",       "Billing & Payments",  "Negombo Bay Inn",       -4,   8,  3),
            ("Minibar charges not posting to room folio automatically",           "Bug",     "Medium",   "Resolved",    "F&B Management",       "Kandy City Resort",    -12,  -1,  2),
            ("Online pre check-in with ID and passport upload",                  "Feature", "High",     "In Progress", "Front Office",         "Grand Colombo Hotel",  -10,  20, 21),
            ("Dynamic pricing integration with revenue management engine",        "Feature", "High",     "To Do",       "Revenue Management",   "Kandy City Resort",     -5,  30, 34),
            ("Housekeeping mobile app for real-time room status updates",         "Feature", "High",     "Active",      "Housekeeping",         "Galle Face Suites",     -8,  15, 28),
            ("Guest self-service portal for requests and bill review",            "Feature", "Medium",   "New",         "Front Office",         "Bentota Beach Club",     0,  45, 18),
            ("WhatsApp integration for automated booking confirmations",          "Feature", "Medium",   "New",         "Reservations",         "Nuwara Eliya Heights",   5,  40, 13),
            ("POS terminal sync with guest room charge posting",                  "Feature", "High",     "In Progress", "F&B Management",       "Grand Colombo Hotel",   -6,  14, 21),
            ("Loyalty program points redemption at express checkout",             "Feature", "Medium",   "To Do",       "Billing & Payments",  "Kandy City Resort",     -3,  21, 13),
            ("Spa booking integration with PMS availability calendar",            "Feature", "Low",      "New",         "Front Office",         "Nuwara Eliya Heights",   8,  60,  8),
            ("As front desk agent I want to upsell room upgrades at check-in",   "Story",   "High",     "In Progress", "Front Office",         "Grand Colombo Hotel",   -4,  10,  8),
            ("As housekeeper I want to mark rooms clean from my mobile device",  "Story",   "High",     "Active",      "Housekeeping",         "Galle Face Suites",     -2,  12,  5),
            ("As manager I want real-time occupancy on the dashboard",           "Story",   "High",     "Done",        "Revenue Management",   "Bentota Beach Club",   -20,  -5,  5),
            ("As guest I want to request extra amenities through the app",       "Story",   "Medium",   "New",         "Front Office",         "Negombo Bay Inn",        3,  25,  3),
            ("As finance officer I want end-of-day revenue report by outlet",    "Story",   "High",     "Resolved",    "Billing & Payments",  "Grand Colombo Hotel",  -15,  -3,  8),
            ("As reservations agent I want to view OTA booking source stats",    "Story",   "Medium",   "To Do",       "Reservations",         "Kandy City Resort",      0,  18,  5),
            ("Configure rate plan for seasonal promotional package",             "Task",    "Medium",   "Done",        "Reservations",         "Galle Face Suites",    -10,  -2,  2),
            ("Update tax codes to comply with revised VAT regulations",          "Task",    "High",     "In Progress", "Billing & Payments",  "Grand Colombo Hotel",   -5,   7,  4),
            ("Create room type mapping for new OTA partner integration",         "Task",    "Medium",   "To Do",       "Reservations",         "Bentota Beach Club",    -1,  10,  3),
            ("Set up minibar consumption auto-posting rules by room category",   "Task",    "Low",      "New",         "F&B Management",       "Nuwara Eliya Heights",   5,  20,  2),
            ("Configure automatic late checkout surcharge posting rule",         "Task",    "Medium",   "Resolved",    "Billing & Payments",  "Negombo Bay Inn",       -8,  -1,  2),
            ("Set up maintenance request escalation and SLA notification rules", "Task",    "Medium",   "Active",      "Maintenance",          "Kandy City Resort",     -3,   9,  3),
            ("Audit user access permissions for all hotel department accounts",  "Task",    "High",     "To Do",       "HR & Payroll",         "Grand Colombo Hotel",    0,  14,  4),
            ("Document SOP for walk-in guest check-in and ID verification",     "Task",    "Low",      "New",         "Front Office",         "Galle Face Suites",      2,  30,  2),
            ("Complete Guest Journey Digitization Initiative",                   "Epic",    "High",     "Active",      "Front Office",         "Grand Colombo Hotel",  -30,  90, 40),
            ("Revenue Management Automation Platform",                           "Epic",    "High",     "In Progress", "Revenue Management",   "Kandy City Resort",    -20, 120, 55),
        };

        var inserted = 0;
        foreach (var s in seeds)
        {
            // Always assign owner to started/finished tasks; 25% chance of unassigned for New/To Do
            var needsOwner = s.Status is "Active" or "In Progress" or "Resolved" or "Done" or "Closed";
            var skipOwner  = !needsOwner && rng.Next(4) == 0;
            var ownerIdx   = rng.Next(members.Count);

            var task = new TaskItem
            {
                Goal      = s.Goal,
                ItemType  = s.Type,
                Priority  = s.Priority,
                Status    = s.Status,
                Project   = s.Module,
                Property  = s.Client,
                StartDate = now.AddDays(s.DsS),
                EndDate   = now.AddDays(s.DsE),
                Weight    = s.Wt,
                Owner     = skipOwner ? null : members[ownerIdx].DisplayName,
                CreatedAt = now.AddDays(rng.Next(-30, 0)),
                CreatedBy = creator,
                Reporter  = creator,
                TeamId    = teamId,
            };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();

            inserted++;
        }

        return Json(new { success = true, count = inserted });
    }
}
