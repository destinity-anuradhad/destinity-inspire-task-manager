using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text;
using TaskTracker.Data;
using TaskTracker.Hubs;
using TaskTracker.Models;
using TaskTracker.Models.ViewModels;
using TaskTracker.Services;

namespace TaskTracker.Controllers;

[Authorize]
public class TasksController(AppDbContext db, IConfiguration config, IHubContext<TaskHub> hub, IEmailService email) : Controller
{
    private readonly AppDbContext      _db    = db;
    private readonly IConfiguration   _config = config;
    private readonly IHubContext<TaskHub> _hub = hub;
    private readonly IEmailService    _email  = email;

    // ── Permission helpers ────────────────────────────────
    private bool IsAdmin() => User.IsInRole("Admin");
    private string CurrentUsername()    => User.Identity?.Name ?? "";
    private string CurrentDisplayName() => User.FindFirst("DisplayName")?.Value ?? "";

    private static IEnumerable<string> SplitOwners(string? owner) =>
        (owner ?? "").Split(',').Select(o => o.Trim()).Where(o => o.Length > 0);

    private bool CanEdit(TaskItem task) => task.Status == "Not Started";

    private bool CanDelete(TaskItem task) => task.Status == "Not Started";

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
        // null (no ?owner= in URL) and "" (?owner= empty) both mean "all owners".
        // Use "My Tasks" button to explicitly filter to the current user.
        var effectiveOwner = owner ?? "";

        var vm = new TaskListViewModel
        {
            Search   = search,   Project  = project,  Property = property,
            Owner    = effectiveOwner, Status = status, Priority = priority,
            DateFrom = dateFrom, DateTo   = dateTo,
            Page     = page,     PageSize = pageSize,
            Sort     = sort,     SortDir  = sortDir,
            ViewMode = view,
            CalYear  = calYear  > 0 ? calYear  : now.Year,
            CalMonth = calMonth > 0 ? calMonth : now.Month,
        };

        var query = _db.Tasks.AsQueryable();

        if (!string.IsNullOrEmpty(vm.Search))
            query = query.Where(t =>
                t.Goal.Contains(vm.Search) || t.Property.Contains(vm.Search) ||
                t.Owner.Contains(vm.Search) || t.Project.Contains(vm.Search));

        if (!string.IsNullOrEmpty(vm.Project))
            query = vm.Project == "__none__"
                ? query.Where(t => t.Project == "")
                : query.Where(t => t.Project == vm.Project);

        if (!string.IsNullOrEmpty(vm.Property))
            query = query.Where(t => t.Property == vm.Property);

        if (!string.IsNullOrEmpty(vm.Owner))
            query = query.Where(t => t.Owner.Contains(vm.Owner));

        // Sort in SQL before loading
        query = (vm.Sort, vm.SortDir) switch
        {
            ("goal",       "asc")  => query.OrderBy(t => t.Goal),
            ("goal",       _)      => query.OrderByDescending(t => t.Goal),
            ("project",    "asc")  => query.OrderBy(t => t.Project),
            ("project",    _)      => query.OrderByDescending(t => t.Project),
            ("property",   "asc")  => query.OrderBy(t => t.Property),
            ("property",   _)      => query.OrderByDescending(t => t.Property),
            ("priority",   "asc")  => query.OrderBy(t => t.Priority),
            ("priority",   _)      => query.OrderByDescending(t => t.Priority),
            ("owner",      "asc")  => query.OrderBy(t => t.Owner),
            ("owner",      _)      => query.OrderByDescending(t => t.Owner),
            ("status",     "asc")  => query.OrderBy(t => t.Status),
            ("status",     _)      => query.OrderByDescending(t => t.Status),
            ("targetDate", "asc")  => query.OrderBy(t => t.TargetDate),
            ("targetDate", _)      => query.OrderByDescending(t => t.TargetDate),
            _                      => query.OrderByDescending(t => t.CreatedAt),
        };

        // Load all (date filter needs in-memory parsing of mixed-format TargetDate strings)
        var allTasks = await query.ToListAsync();

        // In-memory date range filter on TargetDate
        if (DateTime.TryParse(vm.DateFrom, out var dfrom))
            allTasks = allTasks.Where(t => DateTime.TryParse(t.TargetDate, out var d) && d.Date >= dfrom.Date).ToList();
        if (DateTime.TryParse(vm.DateTo, out var dto))
            allTasks = allTasks.Where(t => DateTime.TryParse(t.TargetDate, out var d) && d.Date <= dto.Date).ToList();

        // Status counts (after date filter, before status filter)
        vm.StatusCounts = allTasks
            .GroupBy(t => t.Status ?? "")
            .ToDictionary(g => g.Key, g => g.Count());

        if (!string.IsNullOrEmpty(vm.Status))
            allTasks = vm.Status == "__none__"
                ? allTasks.Where(t => t.Status == "").ToList()
                : allTasks.Where(t => t.Status == vm.Status).ToList();

        if (!string.IsNullOrEmpty(vm.Priority))
            allTasks = allTasks.Where(t => t.Priority == vm.Priority).ToList();

        vm.TotalCount = allTasks.Count;

        if (vm.ViewMode is "kanban" or "calendar")
        {
            vm.Tasks = allTasks;
        }
        else
        {
            vm.Tasks = allTasks
                .Skip((vm.Page - 1) * vm.PageSize)
                .Take(vm.PageSize)
                .ToList();
        }

        vm.Projects    = await _db.Projects.OrderBy(p => p.Name).ToListAsync();
        vm.Properties  = await _db.Properties.OrderBy(p => p.Name).ToListAsync();
        vm.Owners      = (await _db.AppUsers.OrderBy(u => u.DisplayName).ToListAsync())
                             .Select(u => new OwnerItem { Id = u.Id, Name = u.DisplayName, Color = u.Color ?? "#094f70" })
                             .ToList();
        vm.WorkStates  = await _db.WorkStates.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
        if (!vm.WorkStates.Any())
            vm.WorkStates = TaskListViewModel.AllStatuses
                .Select((s, i) => new WorkState { Id = i, Name = s, Color = "#094f70", SortOrder = i })
                .ToList();

        if (view == "team")
        {
            var allForTl = await _db.Tasks.ToListAsync();
            DateTime tlFrom, tlTo;
            string resolvedMode;
            if (tlmode == "today")
            {
                tlFrom = tlTo = now.Date;
                resolvedMode = "today";
            }
            else if (!string.IsNullOrEmpty(tlfrom) && DateTime.TryParse(tlfrom, out var fd) &&
                     !string.IsNullOrEmpty(tlto)   && DateTime.TryParse(tlto,   out var td))
            {
                tlFrom = fd <= td ? fd : td;
                tlTo   = fd <= td ? td : fd;
                resolvedMode = "range";
            }
            else if (calYear > 0 && calMonth >= 1 && calMonth <= 12)
            {
                tlFrom = new DateTime(calYear, calMonth, 1);
                tlTo   = tlFrom.AddMonths(1).AddDays(-1);
                resolvedMode = "range";
            }
            else
            {
                int dow = (int)now.DayOfWeek;
                tlFrom = now.Date.AddDays(dow == 0 ? -6 : 1 - dow);
                tlTo   = tlFrom.AddDays(4);
                resolvedMode = "week";
            }
            var appUsers = await _db.AppUsers.OrderBy(u => u.DisplayName).ToListAsync();
            var regOwners = appUsers.Select(u => u.DisplayName).Where(n => !string.IsNullOrEmpty(n)).ToList();
            var extraOwners = allForTl
                .Where(t => !string.IsNullOrEmpty(t.Owner))
                .SelectMany(t => t.Owner!.Split(',').Select(o => o.Trim()))
                .Where(o => o.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(o => !regOwners.Any(r => string.Equals(r, o, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var projects   = await _db.Projects.ToListAsync();
            var properties = await _db.Properties.ToListAsync();
            ViewBag.TlFrom           = tlFrom.ToString("yyyy-MM-dd");
            ViewBag.TlTo             = tlTo.ToString("yyyy-MM-dd");
            ViewBag.TlMode           = resolvedMode;
            ViewBag.TlOwners         = System.Text.Json.JsonSerializer.Serialize(regOwners.Concat(extraOwners).ToList());
            ViewBag.TlProjectColors  = System.Text.Json.JsonSerializer.Serialize(
                projects.Where(p => !string.IsNullOrEmpty(p.Color))
                        .ToDictionary(p => p.Name ?? "", p => p.Color ?? "#6b7280"));
            ViewBag.TlPropertyColors = System.Text.Json.JsonSerializer.Serialize(
                properties.Where(p => !string.IsNullOrEmpty(p.Color))
                          .ToDictionary(p => p.Name ?? "", p => p.Color ?? "#6b7280"));
            ViewBag.TlTasks = System.Text.Json.JsonSerializer.Serialize(allForTl.Select(t => new
            {
                id              = t.Id,
                goal            = t.Goal            ?? "",
                owner           = t.Owner           ?? "",
                status          = t.Status          ?? "",
                project         = t.Project         ?? "",
                property        = t.Property        ?? "",
                startDate       = t.StartDate       ?? "",
                endDate         = t.EndDate         ?? "",
                actualStartDate = t.ActualStartDate ?? "",
                actualEndDate   = t.ActualEndDate   ?? "",
            }));
        }

        ViewData["ActiveNav"] = "Tasks";
        return View(vm);
    }

    // GET /Tasks/Edit/{id}
    public async Task<IActionResult> Edit(int id)
    {
        var task = await _db.Tasks.FindAsync(id);
        if (task == null) return NotFound();
        if (!CanEdit(task))
        {
            TempData["Error"] = "You don't have permission to edit this task.";
            return RedirectToAction(nameof(Index));
        }
        await LoadEditDropdowns();
        ViewData["ActiveNav"] = "Tasks";
        return View(task);
    }

    // POST /Tasks/Edit/{id}
    [HttpPost][ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, TaskItem task)
    {
        var existing = await _db.Tasks.FindAsync(id);
        if (existing == null) return NotFound();
        if (!CanEdit(existing))
        {
            TempData["Error"] = "You don't have permission to edit this task.";
            return RedirectToAction(nameof(Index));
        }

        var changes  = new List<string>();
        var oldOwner = existing.Owner;
        if (existing.Status   != task.Status)   changes.Add($"Status: '{existing.Status}' → '{task.Status}'");
        if (existing.Priority != task.Priority) changes.Add($"Priority: '{existing.Priority}' → '{task.Priority}'");
        if (existing.Owner    != task.Owner)    changes.Add($"Owner: '{existing.Owner}' → '{task.Owner}'");

        existing.Goal = task.Goal; existing.Project = task.Project;
        existing.Property = task.Property; existing.TargetDate = task.TargetDate;
        existing.Priority = task.Priority; existing.Owner = task.Owner;
        existing.Status = task.Status; existing.StartDate = task.StartDate;
        existing.EndDate = task.EndDate; existing.ActualStartDate = task.ActualStartDate;
        existing.ActualEndDate = task.ActualEndDate; existing.RelatedFiles = task.RelatedFiles;
        existing.Notes = task.Notes; existing.Weight = task.Weight;

        await _db.SaveChangesAsync();
        var details = changes.Count > 0 ? string.Join("; ", changes) : "Updated";
        await TryLogAsync(id, existing.Goal, existing.Project ?? "", changes.Any(c => c.StartsWith("Status")) ? "Status Changed" : "Updated", details);
        await NotifyAssigned(existing, oldOwner);

        TempData["Success"] = "Task updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadEditDropdowns()
    {
        ViewBag.Projects    = await _db.Projects.OrderBy(p => p.Name).ToListAsync();
        ViewBag.Properties  = await _db.Properties.OrderBy(p => p.Name).ToListAsync();
        ViewBag.Owners      = (await _db.AppUsers.OrderBy(u => u.DisplayName).ToListAsync())
                                  .Select(u => new OwnerItem { Id = u.Id, Name = u.DisplayName, Color = u.Color ?? "#094f70" })
                                  .ToList();
        var ws = await _db.WorkStates.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
        ViewBag.WorkStates  = ws.Any() ? ws
            : TaskListViewModel.AllStatuses.Select((s, i) => new WorkState { Id = i, Name = s, Color = "#094f70", SortOrder = i }).ToList();
    }

    // POST /Tasks/Save — create (Id==0) or update
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(TaskItem task)
    {
        try
        {
            string action, details;
            if (task.Id == 0)
            {
                task.CreatedAt = DateTime.Now;
                task.CreatedBy = CurrentUsername();
                if (string.IsNullOrWhiteSpace(task.TargetDate))
                    task.TargetDate = task.CreatedAt.ToString("yyyy-MM-dd");
                _db.Tasks.Add(task);
                await _db.SaveChangesAsync();
                action  = "Created";
                details = $"Goal: {task.Goal}";
                await NotifyAssigned(task, null);
            }
            else
            {
                var existing = await _db.Tasks.FindAsync(task.Id);
                if (existing == null) return Json(new { success = false, error = "Task not found" });

                if (!CanEdit(existing))
                    return Json(new { success = false, error = "You don't have permission to edit this task." });

                var changes    = new List<string>();
                var oldOwner   = existing.Owner;
                if (existing.Goal     != task.Goal)     changes.Add("Goal");
                if (existing.Status   != task.Status)   changes.Add($"Status: '{existing.Status}' → '{task.Status}'");
                if (existing.Priority != task.Priority) changes.Add($"Priority: '{existing.Priority}' → '{task.Priority}'");
                if (existing.Owner    != task.Owner)    changes.Add($"Owner: '{existing.Owner}' → '{task.Owner}'");

                existing.Goal = task.Goal; existing.Project = task.Project;
                existing.Property = task.Property;
                existing.TargetDate = string.IsNullOrWhiteSpace(task.TargetDate)
                    ? existing.CreatedAt.ToString("yyyy-MM-dd") : task.TargetDate;
                existing.Priority = task.Priority; existing.Owner = task.Owner;
                existing.Status = task.Status; existing.StartDate = task.StartDate;
                existing.EndDate = task.EndDate; existing.ActualStartDate = task.ActualStartDate;
                existing.ActualEndDate = task.ActualEndDate; existing.RelatedFiles = task.RelatedFiles;
                existing.Notes = task.Notes; existing.Weight = task.Weight;
                existing.ModuleId = task.ModuleId; existing.ClientId = task.ClientId;
                existing.ItemType = task.ItemType; existing.ParentId = task.ParentId;
                existing.SprintId = task.SprintId;

                await _db.SaveChangesAsync();
                action  = "Updated";
                details = changes.Count > 0 ? string.Join("; ", changes) : "Minor update";
                await NotifyAssigned(existing, oldOwner);
            }

            await TryLogAsync(task.Id, task.Goal, task.Project ?? "", action, details);
            await _hub.Clients.All.SendAsync("TaskUpdated", new {
                id        = task.Id,
                goal      = task.Goal,
                status    = task.Status,
                priority  = task.Priority,
                owner     = task.Owner,
                itemType  = task.ItemType,
                sprintId  = task.SprintId,
                updatedBy = CurrentDisplayName()
            });
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    // GET /Tasks/GetComments?taskId=X
    [HttpGet]
    public async Task<IActionResult> GetComments(int taskId)
    {
        var comments = await _db.ActivityLog
            .Where(a => a.TaskId == taskId && a.Action == "Comment")
            .OrderBy(a => a.ChangedAt)
            .Select(a => new {
                a.ChangedBy,
                a.Details,
                changedAt    = a.ChangedAt.ToString("dd MMM yyyy HH:mm"),
                changedAtIso = a.ChangedAt.ToString("o")
            })
            .ToListAsync();
        return Json(comments);
    }

    // POST /Tasks/AddComment
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddComment(int taskId, string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return Json(new { success = false, error = "Comment cannot be empty." });
        var task = await _db.Tasks.FindAsync(taskId);
        if (task == null) return Json(new { success = false, error = "Task not found." });
        await TryLogAsync(taskId, task.Goal, task.Project ?? "", "Comment", comment.Trim());
        await _hub.Clients.Group("task-" + taskId).SendAsync("CommentAdded", taskId);
        _ = NotifyCommentAsync(task, comment.Trim());
        return Json(new { success = true });
    }

    // GET /Tasks/GetModules — returns active projects from him.scienter.lk
    [HttpGet]
    public async Task<IActionResult> GetModules()
    {
        var list = new List<object>();
        var cs   = _config.GetConnectionString("HimConnection");
        await using var con = new SqlConnection(cs);
        await con.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT Id, Code, Name FROM Projects WHERE IsActive = 1 ORDER BY Name", con);
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new { id = rdr.GetInt32(0), code = rdr.GetString(1), name = rdr.GetString(2) });
        return Json(list);
    }

    // GET /Tasks/GetBacklog?sprintFilter=all|backlog|{id} — all tasks for backlog tree view
    [HttpGet]
    public async Task<IActionResult> GetBacklog(string sprintFilter = "all")
    {
        var query = _db.Tasks.AsQueryable();

        // Non-admins only see tasks they own
        if (!User.IsInRole("Admin"))
        {
            var me = User.FindFirst("DisplayName")?.Value ?? "";
            query = query.Where(t => t.Owner != null && t.Owner.Contains(me));
        }

        // Sprint filter
        if (sprintFilter == "backlog")
            query = query.Where(t => t.SprintId == null);
        else if (int.TryParse(sprintFilter, out var sid) && sid > 0)
            query = query.Where(t => t.SprintId == sid);

        var tasks   = await query.OrderBy(t => t.CreatedAt).ToListAsync();
        var sprints = await _db.Sprints.OrderByDescending(s => s.IsActive).ThenByDescending(s => s.CreatedAt).ToListAsync();
        var opts    = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        return Json(new { tasks, sprints }, opts);
    }

    // GET /Tasks/GetBurndown?sprintId=X — burndown data for a sprint
    [HttpGet]
    public async Task<IActionResult> GetBurndown(int sprintId)
    {
        var sprint = await _db.Sprints.FindAsync(sprintId);
        if (sprint == null) return NotFound();

        var taskIds = await _db.Tasks
            .Where(t => t.SprintId == sprintId)
            .Select(t => t.Id)
            .ToListAsync();

        var total = taskIds.Count;

        // Parse sprint date range
        var doneStatuses = new[] { "Done", "Resolved", "Closed" };
        DateTime start = DateTime.TryParse(sprint.StartDate, out var s) ? s.Date : DateTime.Today.AddDays(-14);
        DateTime end   = DateTime.TryParse(sprint.EndDate,   out var e) ? e.Date : DateTime.Today;
        if (end < start) end = start.AddDays(14);

        // Get first "done" status change per task from ActivityLog
        var completions = await _db.ActivityLog
            .Where(a => taskIds.Contains(a.TaskId)
                     && a.Action == "Status Changed"
                     && (a.Details.Contains("'Done'") || a.Details.Contains("'Resolved'") || a.Details.Contains("'Closed'")))
            .Select(a => new { a.TaskId, a.ChangedAt })
            .ToListAsync();

        var firstDone = completions
            .GroupBy(c => c.TaskId)
            .ToDictionary(g => g.Key, g => g.Min(c => c.ChangedAt).Date);

        // Build day-by-day remaining count
        var days = new List<object>();
        var ideal = new List<object>();
        int totalDays = Math.Max(1, (end - start).Days + 1);

        for (int i = 0; i < totalDays; i++)
        {
            var day = start.AddDays(i);
            int doneSoFar = firstDone.Values.Count(d => d <= day);
            int remaining = total - doneSoFar;
            double idealRemaining = total - (total * i / (double)(totalDays - 1 < 1 ? 1 : totalDays - 1));

            days.Add(new { date = day.ToString("MMM dd"), remaining });
            ideal.Add(new { date = day.ToString("MMM dd"), remaining = Math.Round(idealRemaining, 1) });
        }

        return Json(new {
            sprintName = sprint.Name,
            total,
            days,
            ideal
        });
    }

    // GET /Tasks/GetSprints — all sprints for canvas dropdown
    [HttpGet]
    public async Task<IActionResult> GetSprints()
    {
        var sprints = await _db.Sprints
            .OrderByDescending(s => s.IsActive)
            .ThenByDescending(s => s.CreatedAt)
            .Select(s => new { id = s.Id, name = s.Name, isActive = s.IsActive, startDate = s.StartDate, endDate = s.EndDate })
            .ToListAsync();
        return Json(sprints);
    }

    // GET /Tasks/GetParentItems?itemType=Feature — returns valid parent items for the given type
    [HttpGet]
    public async Task<IActionResult> GetParentItems(string itemType = "")
    {
        var parentType = itemType switch {
            "Feature" => "Epic",
            "Story"   => "Feature",
            "Task"    => "Story",
            "Bug"     => "Story",
            _         => ""
        };
        if (string.IsNullOrEmpty(parentType))
            return Json(Array.Empty<object>());

        var items = await _db.Tasks
            .Where(t => t.ItemType == parentType)
            .OrderBy(t => t.Goal)
            .Select(t => new { id = t.Id, goal = t.Goal, status = t.Status })
            .ToListAsync();
        return Json(items);
    }

    // GET /Tasks/SearchTasks?q=X — search by ID or title (up to 10 results)
    [HttpGet]
    public async Task<IActionResult> SearchTasks(string q = "")
    {
        if (string.IsNullOrWhiteSpace(q)) return Json(Array.Empty<object>());
        IQueryable<TaskItem> query;
        if (int.TryParse(q.Trim(), out var id))
            query = _db.Tasks.Where(t => t.Id == id || (t.Goal != null && t.Goal.Contains(q)));
        else
            query = _db.Tasks.Where(t => t.Goal != null && t.Goal.Contains(q));
        var results = await query
            .OrderBy(t => t.Goal)
            .Take(10)
            .Select(t => new { id = t.Id, goal = t.Goal, status = t.Status, itemType = t.ItemType })
            .ToListAsync();
        return Json(results);
    }

    // GET /Tasks/GetLinks?taskId=X — all links for a task (bidirectional)
    [HttpGet]
    public async Task<IActionResult> GetLinks(int taskId)
    {
        var links = await _db.TaskLinks
            .Where(l => l.FromTaskId == taskId || l.ToTaskId == taskId)
            .ToListAsync();
        var linkedIds = links.SelectMany(l => new[] { l.FromTaskId, l.ToTaskId })
            .Distinct().Where(x => x != taskId).ToList();
        var tasks = await _db.Tasks
            .Where(t => linkedIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Goal, t.Status, t.ItemType })
            .ToDictionaryAsync(t => t.Id);
        var result = links.Select(l => {
            var isFrom   = l.FromTaskId == taskId;
            var linkedId = isFrom ? l.ToTaskId : l.FromTaskId;
            tasks.TryGetValue(linkedId, out var linked);
            return new {
                id               = l.Id,
                linkType         = l.LinkType,
                isFrom,
                linkedTaskId     = linkedId,
                linkedTaskGoal   = linked?.Goal   ?? "Unknown",
                linkedTaskStatus = linked?.Status  ?? "",
                linkedTaskItemType = linked?.ItemType ?? ""
            };
        }).ToList();
        return Json(result);
    }

    // POST /Tasks/AddLink
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddLink(int fromTaskId, int toTaskId, string linkType)
    {
        if (fromTaskId == toTaskId)
            return BadRequest(new { message = "Cannot link a task to itself." });
        var exists = await _db.TaskLinks.AnyAsync(l =>
            (l.FromTaskId == fromTaskId && l.ToTaskId == toTaskId && l.LinkType == linkType) ||
            (l.FromTaskId == toTaskId   && l.ToTaskId == fromTaskId && l.LinkType == linkType));
        if (exists)
            return BadRequest(new { message = "This link already exists." });
        var link = new TaskLink {
            FromTaskId = fromTaskId,
            ToTaskId   = toTaskId,
            LinkType   = linkType,
            CreatedAt  = DateTime.Now,
            CreatedBy  = User.FindFirst("DisplayName")?.Value ?? User.Identity?.Name
        };
        _db.TaskLinks.Add(link);
        await _db.SaveChangesAsync();
        await _hub.Clients.Group("task-" + fromTaskId).SendAsync("LinkUpdated", fromTaskId);
        await _hub.Clients.Group("task-" + toTaskId).SendAsync("LinkUpdated", toTaskId);
        if (linkType == "Blocks")
            _ = NotifyBlockedAsync(fromTaskId, toTaskId);
        return Ok();
    }

    // POST /Tasks/RemoveLink?id=X
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveLink(int id)
    {
        var link = await _db.TaskLinks.FindAsync(id);
        if (link == null) return NotFound();
        int from = link.FromTaskId, to = link.ToTaskId;
        _db.TaskLinks.Remove(link);
        await _db.SaveChangesAsync();
        await _hub.Clients.Group("task-" + from).SendAsync("LinkUpdated", from);
        await _hub.Clients.Group("task-" + to).SendAsync("LinkUpdated", to);
        return Ok();
    }

    // GET /Tasks/GetClients?moduleId=X — clients for module (0 = all clients); CompanyDetails preferred
    [HttpGet]
    public async Task<IActionResult> GetClients(int moduleId = 0)
    {
        var list = new List<object>();
        var cs   = _config.GetConnectionString("HimConnection");
        await using var con = new SqlConnection(cs);
        await con.OpenAsync();

        // UNION approach:
        //   Part 1 — one row per distinct valid company (clients that belong to a real company)
        //   Part 2 — individual clients that have no valid company (CompanyId null/0 or CompanyDetails.Name blank/"N/A")
        // "Valid company" = CompanyDetails exists AND Name is not empty/blank/"N/A"
        // moduleId 0 = no module filter → load all active clients.
        var sql = moduleId > 0
            ? @"SELECT DISTINCT cd.Id AS Id, cd.Name AS Name,
                    cd.ContactPerson, cd.ContactNo01, cd.Email01, 1 AS FromCompany
                FROM Clients c
                JOIN CompanyDetails cd ON cd.Id = c.CompanyId
                WHERE c.IsActive = 1
                  AND NULLIF(NULLIF(cd.Name,''),'N/A') IS NOT NULL
                  AND EXISTS (SELECT 1 FROM ClientWiseProjects cwp WHERE cwp.ClientId = c.Id AND cwp.ProjectId = @mid)
                UNION
                SELECT c.Id AS Id, c.Name AS Name,
                    c.ContactPerson, c.ContactNo01, c.Email01, 0 AS FromCompany
                FROM Clients c
                LEFT JOIN CompanyDetails cd ON cd.Id = c.CompanyId
                WHERE c.IsActive = 1
                  AND NULLIF(NULLIF(cd.Name,''),'N/A') IS NULL
                  AND EXISTS (SELECT 1 FROM ClientWiseProjects cwp WHERE cwp.ClientId = c.Id AND cwp.ProjectId = @mid)
                ORDER BY Name"
            : @"SELECT DISTINCT cd.Id AS Id, cd.Name AS Name,
                    cd.ContactPerson, cd.ContactNo01, cd.Email01, 1 AS FromCompany
                FROM Clients c
                JOIN CompanyDetails cd ON cd.Id = c.CompanyId
                WHERE c.IsActive = 1
                  AND NULLIF(NULLIF(cd.Name,''),'N/A') IS NOT NULL
                UNION
                SELECT c.Id AS Id, c.Name AS Name,
                    c.ContactPerson, c.ContactNo01, c.Email01, 0 AS FromCompany
                FROM Clients c
                LEFT JOIN CompanyDetails cd ON cd.Id = c.CompanyId
                WHERE c.IsActive = 1
                  AND NULLIF(NULLIF(cd.Name,''),'N/A') IS NULL
                ORDER BY Name";

        await using var cmd = new SqlCommand(sql, con);
        if (moduleId > 0) cmd.Parameters.AddWithValue("@mid", moduleId);
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new {
                id            = rdr.GetInt32(0),
                name          = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                contactPerson = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                contactNo     = rdr.IsDBNull(3) ? "" : rdr.GetString(3),
                email         = rdr.IsDBNull(4) ? "" : rdr.GetString(4),
                fromCompany   = rdr.GetInt32(5) == 1
            });
        return Json(list);
    }

    // POST /Tasks/UpdateStatus — inline status change / kanban drag
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, string status)
    {
        var task = await _db.Tasks.FindAsync(id);
        if (task == null) return Json(new { success = false, error = "Not found" });
        var old = task.Status;
        task.Status = status;
        await _db.SaveChangesAsync();
        await TryLogAsync(id, task.Goal, task.Project ?? "", "Status Changed", $"'{old}' → '{status}'");
        await _hub.Clients.All.SendAsync("TaskUpdated", new {
            id        = task.Id,
            goal      = task.Goal,
            status    = task.Status,
            priority  = task.Priority,
            owner     = task.Owner,
            itemType  = task.ItemType,
            sprintId  = task.SprintId,
            updatedBy = CurrentDisplayName()
        });
        return Json(new { success = true });
    }

    // POST /Tasks/Delete/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var task = await _db.Tasks.FindAsync(id);
        if (task != null)
        {
            if (!CanDelete(task))
            {
                TempData["Error"] = "You can only delete tasks you created.";
                return RedirectToAction(nameof(Index));
            }
            await TryLogAsync(id, task.Goal, task.Project ?? "", "Deleted", "");
            _db.Tasks.Remove(task);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    // GET /Tasks/Export — download filtered tasks as CSV
    [HttpGet]
    public async Task<IActionResult> Export(
        string search = "", string project = "", string property = "",
        string? owner = null, string status = "", string priority = "",
        string dateFrom = "", string dateTo = "")
    {
        var query = _db.Tasks.AsQueryable();

        if (!string.IsNullOrEmpty(search))
            query = query.Where(t => t.Goal.Contains(search) || t.Property.Contains(search) ||
                                     t.Owner.Contains(search) || t.Project.Contains(search));
        if (!string.IsNullOrEmpty(project))
            query = project == "__none__" ? query.Where(t => t.Project == "") : query.Where(t => t.Project == project);
        if (!string.IsNullOrEmpty(property)) query = query.Where(t => t.Property == property);
        if (!string.IsNullOrEmpty(owner))    query = query.Where(t => t.Owner.Contains(owner));
        if (!string.IsNullOrEmpty(status))
            query = status == "__none__" ? query.Where(t => t.Status == "") : query.Where(t => t.Status == status);
        if (!string.IsNullOrEmpty(priority)) query = query.Where(t => t.Priority == priority);

        var tasks = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();

        if (DateTime.TryParse(dateFrom, out var dfrom))
            tasks = tasks.Where(t => DateTime.TryParse(t.TargetDate, out var d) && d.Date >= dfrom.Date).ToList();
        if (DateTime.TryParse(dateTo, out var dto))
            tasks = tasks.Where(t => DateTime.TryParse(t.TargetDate, out var d) && d.Date <= dto.Date).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("Id,Goal,Project,Client,Priority,Owner,Status,TargetDate,ExpectedStartDate,ExpectedEndDate,ActualStartDate,ActualEndDate,Weight,RelatedFiles,Notes,CreatedAt");
        foreach (var t in tasks)
            sb.AppendLine($"{t.Id},{Csv(t.Goal)},{Csv(t.Project)},{Csv(t.Property)},{Csv(t.Priority)},{Csv(t.Owner)},{Csv(t.Status)},{Csv(t.TargetDate)},{Csv(t.StartDate)},{Csv(t.EndDate)},{Csv(t.ActualStartDate)},{Csv(t.ActualEndDate)},{t.Weight},{Csv(t.RelatedFiles)},{Csv(t.Notes)},{t.CreatedAt:yyyy-MM-dd HH:mm}");

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"tasks-{DateTime.Now:yyyyMMdd}.csv");
    }

    private static string Csv(string? s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";

    private async Task NotifyAssigned(TaskItem task, string? previousOwner)
    {
        try
        {
            var prevOwners = SplitOwners(previousOwner).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newOwners  = SplitOwners(task.Owner)
                .Where(o => !prevOwners.Contains(o))
                .ToList();
            foreach (var ownerName in newOwners)
            {
                var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.DisplayName == ownerName);
                if (user == null || user.Username == CurrentUsername()) continue;
                if (user.NotifyOnAssigned)
                    _db.Notifications.Add(new Notification {
                        UserId = user.Id, Message = $"You were assigned to: {task.Goal}", CreatedAt = DateTime.Now
                    });
                if (user.NotifyOnAssigned && !string.IsNullOrWhiteSpace(user.Email))
                    _ = _email.SendAsync(user.Email, user.DisplayName,
                        $"[Task Manager] Assigned: {task.Goal}",
                        EmailHtml($"You have been assigned to a task.",
                            $"<b>{HtmlEnc(task.Goal)}</b>", task.Id, "View Task"));
            }
            await _db.SaveChangesAsync();
        }
        catch { }
    }

    private async Task NotifyCommentAsync(TaskItem task, string comment)
    {
        try
        {
            var commenter = CurrentDisplayName();
            foreach (var ownerName in SplitOwners(task.Owner))
            {
                if (string.Equals(ownerName, commenter, StringComparison.OrdinalIgnoreCase)) continue;
                var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.DisplayName == ownerName);
                if (user == null || !user.NotifyOnComment || string.IsNullOrWhiteSpace(user.Email)) continue;
                await _email.SendAsync(user.Email, user.DisplayName,
                    $"[Task Manager] New comment on: {task.Goal}",
                    EmailHtml($"<b>{HtmlEnc(commenter)}</b> commented on a task you own.",
                        $"<blockquote style='border-left:3px solid #e2e8f0;margin:0;padding:8px 12px;color:#475569'>{HtmlEnc(comment)}</blockquote>",
                        task.Id, "View Task"));
            }
        }
        catch { }
    }

    private async Task NotifyBlockedAsync(int fromId, int toId)
    {
        try
        {
            var blocker = await _db.Tasks.Select(t => new { t.Id, t.Goal }).FirstOrDefaultAsync(t => t.Id == fromId);
            var blocked = await _db.Tasks.Select(t => new { t.Id, t.Goal, t.Owner }).FirstOrDefaultAsync(t => t.Id == toId);
            if (blocked == null) return;
            foreach (var ownerName in SplitOwners(blocked.Owner))
            {
                var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.DisplayName == ownerName);
                if (user == null || !user.NotifyOnBlocked || string.IsNullOrWhiteSpace(user.Email)) continue;
                await _email.SendAsync(user.Email, user.DisplayName,
                    $"[Task Manager] Task blocked: {blocked.Goal}",
                    EmailHtml($"Your task has been marked as <b>Blocked By</b> another task.",
                        $"<p><b>Blocked:</b> #{blocked.Id} {HtmlEnc(blocked.Goal)}</p><p><b>Blocker:</b> #{blocker?.Id} {HtmlEnc(blocker?.Goal ?? "")}</p>",
                        blocked.Id, "View Blocked Task"));
            }
        }
        catch { }
    }

    private static string HtmlEnc(string? s) =>
        System.Net.WebUtility.HtmlEncode(s ?? "");

    private static string EmailHtml(string heading, string body, int taskId, string ctaLabel) => $@"
        <div style='font-family:Inter,system-ui,sans-serif;max-width:600px;margin:0 auto;background:#f8fafc;padding:24px'>
          <div style='background:#fff;border-radius:12px;padding:28px 32px;border:1px solid #e2e8f0'>
            <p style='color:#64748b;font-size:13px;margin:0 0 16px'>Scienter Task Manager</p>
            <p style='color:#0f172a;font-size:16px;margin:0 0 16px'>{heading}</p>
            <div style='margin:0 0 20px'>{body}</div>
            <a href='http://localhost:5043/Tasks?highlight={taskId}' style='display:inline-block;background:#2563eb;color:#fff;padding:10px 20px;border-radius:8px;text-decoration:none;font-size:14px;font-weight:600'>{ctaLabel}</a>
          </div>
          <p style='color:#94a3b8;font-size:11px;text-align:center;margin:16px 0 0'>You received this because you are an owner of this task. Update preferences in your Profile.</p>
        </div>";

    private async Task TryLogAsync(int taskId, string taskGoal, string project, string action, string details)
    {
        try
        {
            _db.ActivityLog.Add(new ActivityLog
            {
                TaskId    = taskId,
                TaskGoal  = taskGoal  ?? "",
                Project   = project   ?? "",
                ChangedBy = CurrentDisplayName(),
                Action    = action,
                Details   = details   ?? "",
                ChangedAt = DateTime.Now
            });
            await _db.SaveChangesAsync();
        }
        catch { }
    }
}
