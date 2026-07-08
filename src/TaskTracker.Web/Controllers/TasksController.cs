using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Models;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

[Authorize]
public class TasksController(ITaskService tasks, IConfiguration config) : AppControllerBase
{

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
        var forceClose = Request.Form["ForceClose"] == "true";
        var r = await tasks.SaveAsync(task, BuildUserCtx(), forceClose);
        if (!r.Success)
        {
            if (r.OpenChildren.HasValue)
                return Json(new { success = false, openChildren = r.OpenChildren, error = r.Error });
            return Json(new { success = false, error = r.Error });
        }
        return Json(new { success = true, capacityWarning = r.CapacityWarning });
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
    public async Task<IActionResult> GetComments(int taskId)
    {
        var result = await tasks.GetCommentsAsync(taskId, ActiveTeamId());
        if (result == null) return NotFound();
        return Json(result);
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
        return Json(new { sprintName = r.SprintName, total = r.Total, days = r.Days, ideal = r.Ideal });
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
        var himCs      = config.GetConnectionString("HimConnection");
        var privileged = User.IsInRole("Admin") ||
                         User.FindFirst("TeamRole")?.Value == "TeamAdmin";
        return Json(await tasks.GetClientsAsync(moduleId, himCs, privileged));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, string status, string? swimlane = null)
    {
        var forceClose = Request.Form["ForceClose"] == "true";
        var r = await tasks.UpdateStatusAsync(id, status, swimlane, BuildUserCtx(), forceClose);
        if (!r.Success)
        {
            if (r.NeedOwner)
                return Json(new { success = false, needOwner = true, error = r.Error });
            if (r.OpenChildren.HasValue)
                return Json(new { success = false, openChildren = r.OpenChildren, error = r.Error });
            return Json(new { success = false, error = r.Error });
        }
        return Json(new { success = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignSprint(int taskId, int? sprintId)
    {
        var r = await tasks.AssignSprintAsync(taskId, sprintId, BuildUserCtx());
        if (!r.Success) return Json(new { success = false, error = r.Error });
        return Json(new { success = true, sprintId = r.SprintId, capacityWarning = r.CapacityWarning });
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

    public async Task<IActionResult> RecycleBin()
    {
        if (!IsAdmin() && User.FindFirst("TeamRole")?.Value != "TeamAdmin") return Forbid();
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
        if (ids == null || ids.Length == 0) return Json(new { success = false, error = "No tasks selected" });
        var r = await tasks.BulkDeleteAsync(ids, BuildUserCtx());
        return Json(new { success = r.Success, count = r.Count, denied = r.Denied });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkChangeStatus([FromBody] BulkStatusRequest req)
    {
        if (req?.Ids == null || req.Ids.Length == 0 || string.IsNullOrEmpty(req.Status))
            return Json(new { success = false, error = "Invalid request" });
        var r = await tasks.BulkChangeStatusAsync(req.Ids, req.Status, BuildUserCtx());
        return Json(new { success = r.Success, count = r.Count, denied = r.Denied, noOwner = r.NoOwner });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkChangeOwner([FromBody] BulkOwnerRequest req)
    {
        if (req?.Ids == null || req.Ids.Length == 0)
            return Json(new { success = false, error = "Invalid request" });
        var r = await tasks.BulkChangeOwnerAsync(req.Ids, req.Owner, BuildUserCtx());
        return Json(new { success = r.Success, count = r.Count, denied = r.Denied });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkAssignSprint([FromBody] BulkSprintRequest req)
    {
        if (req?.Ids == null || req.Ids.Length == 0)
            return Json(new { success = false, error = "Invalid request" });
        var r = await tasks.BulkAssignSprintAsync(req.Ids, req.SprintId, BuildUserCtx());
        return Json(new { success = r.Success, count = r.Count, skipped = r.Skipped, denied = r.Denied });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkChangePriority([FromBody] BulkPriorityRequest req)
    {
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

        var allowed = new[] { ".pdf",".doc",".docx",".xls",".xlsx",".png",".jpg",".jpeg",".gif",".webp",".zip",".txt",".csv",".md" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowed.Contains(ext))
            return Json(new { success = false, error = "File type not allowed." });

        var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "attachments", taskId.ToString());
        Directory.CreateDirectory(dir);

        var storedName = Guid.NewGuid().ToString("N") + ext;
        var path       = Path.Combine(dir, storedName);
        await using (var stream = new FileStream(path, FileMode.Create))
            await file.CopyToAsync(stream);

        var displayName = User.FindFirst("DisplayName")?.Value ?? "";
        var (ok, error) = await tasks.SaveAttachmentRecordAsync(taskId, file.FileName, storedName, file.Length, file.ContentType, displayName);
        if (!ok)
        {
            System.IO.File.Delete(path);
            return Json(new { success = false, error });
        }
        return Json(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> DownloadAttachment(int id)
    {
        var att = await tasks.GetDownloadAsync(id, ActiveTeamId());
        if (att == null) return NotFound();
        if (!System.IO.File.Exists(att.FilePath)) return NotFound();
        return PhysicalFile(att.FilePath, att.ContentType, att.FileName);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAttachment(int id)
    {
        var att = await tasks.GetDownloadAsync(id, ActiveTeamId());
        if (att == null) return Json(new { success = false, error = "Not found." });

        var displayName = User.FindFirst("DisplayName")?.Value ?? "";
        var (ok, error) = await tasks.DeleteAttachmentAsync(id, displayName, IsAdmin());
        if (!ok) return Json(new { success = false, error });

        if (System.IO.File.Exists(att.FilePath))
            System.IO.File.Delete(att.FilePath);

        return Json(new { success = true });
    }
}
