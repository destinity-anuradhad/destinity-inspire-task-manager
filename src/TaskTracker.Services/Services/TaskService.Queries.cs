using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text;
using System.Text.Json;
using TaskTracker.Data;
using TaskTracker.Models;
using TaskTracker.Models.ViewModels;

namespace TaskTracker.Services;

public partial class TaskService
{
    public async Task<List<TaskChecklistItem>?> GetChecklistAsync(int taskId, int? teamId)
    {
        if (await FindTaskInTeam(taskId, teamId) == null) return null;
        return await _db.TaskChecklistItems.Where(i => i.TaskId == taskId)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToListAsync();
    }

    // ── ITaskService: query methods ────────────────────────────────────────────

    public async Task<TaskListResult> GetListAsync(TaskListFilters f, int? teamId)
    {
        var now = DateTime.Now;
        var vm = new TaskListViewModel {
            Search   = f.Search,    Project  = f.Project,   Property = f.Property,
            Owner    = f.Owner ?? "", Status = f.Status,    Priority = f.Priority,
            DateFrom = f.DateFrom,  DateTo   = f.DateTo,
            Page     = f.Page,      PageSize = f.PageSize,
            Sort     = f.Sort,      SortDir  = f.SortDir,
            ViewMode = f.View,
            CalYear  = f.CalYear  > 0 ? f.CalYear  : now.Year,
            CalMonth = f.CalMonth > 0 ? f.CalMonth : now.Month,
        };

        var query = _db.Tasks.AsNoTracking()
            .AsQueryable();
        if (teamId.HasValue) query = query.Where(t => t.TeamId == teamId);

        if (!string.IsNullOrEmpty(vm.Search))
            query = query.Where(t =>
                (t.Goal     != null && t.Goal.Contains(vm.Search))     ||
                (t.Property != null && t.Property.Contains(vm.Search)) ||
                (t.Project  != null && t.Project.Contains(vm.Search))  ||
                (t.Labels   != null && t.Labels.Contains(vm.Search))   ||
                (t.Owner    != null && t.Owner.Contains(vm.Search)));

        if (!string.IsNullOrEmpty(vm.Project))
            query = vm.Project == "__none__"
                ? query.Where(t => t.Project == "")
                : query.Where(t => t.Project == vm.Project);

        if (!string.IsNullOrEmpty(vm.Property))
            query = query.Where(t => t.Property == vm.Property);

        if (!string.IsNullOrEmpty(vm.Owner))
            query = query.Where(t => t.Owner != null && t.Owner.Contains(vm.Owner));

        // Date filters pushed to SQL (TargetDate is now DateTime?)
        if (DateTime.TryParse(vm.DateFrom, out var dfrom))
            query = query.Where(t => t.TargetDate >= dfrom.Date);
        if (DateTime.TryParse(vm.DateTo, out var dto))
            query = query.Where(t => t.TargetDate <= dto.Date);

        // Status counts before the status filter so the sidebar shows counts for all statuses
        vm.StatusCounts = (await query
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync())
            .ToDictionary(x => x.Status ?? "", x => x.Count);

        if (!string.IsNullOrEmpty(vm.Status))
            query = vm.Status == "__none__"
                ? query.Where(t => t.Status == "")
                : query.Where(t => t.Status == vm.Status);

        if (!string.IsNullOrEmpty(vm.Priority))
            query = query.Where(t => t.Priority == vm.Priority);

        query = (vm.Sort, vm.SortDir) switch {
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
            ("created",    "asc")  => query.OrderBy(t => t.CreatedAt),
            ("created",    _)      => query.OrderByDescending(t => t.CreatedAt),
            ("weight",     "asc")  => query.OrderBy(t => t.Weight),
            ("weight",     _)      => query.OrderByDescending(t => t.Weight),
            ("actual",     "asc")  => query.OrderBy(t => t.ActualHours),
            ("actual",     _)      => query.OrderByDescending(t => t.ActualHours),
            _                      => query.OrderByDescending(t => t.CreatedAt),
        };

        vm.TotalCount = await query.CountAsync();

        // Kanban and calendar need the full set; table/list paginates in SQL
        vm.Tasks = vm.ViewMode is "kanban" or "calendar"
            ? await query.ToListAsync()
            : await query.Skip((vm.Page - 1) * vm.PageSize).Take(vm.PageSize).ToListAsync();

        if (vm.ViewMode == "table")
        {
            var pageIds = vm.Tasks.Select(t => t.Id).ToList();
            vm.CommentCounts = await _db.ActivityLog
                .Where(a => a.TaskId.HasValue && pageIds.Contains(a.TaskId.Value) && a.Action == "Comment")
                .GroupBy(a => a.TaskId!.Value)
                .Select(g => new { TaskId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TaskId, x => x.Count);
        }

        vm.Projects = await _cache.GetOrCreateAsync("lookup:projects", async e => {
            e.AbsoluteExpirationRelativeToNow = _lookupTtl;
            return await _db.Projects.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
        }) ?? [];

        var propCacheKey = teamId.HasValue ? $"lookup:clientnames:{teamId}" : "lookup:clientnames:global";
        vm.Properties = await _cache.GetOrCreateAsync(propCacheKey, async e => {
            e.AbsoluteExpirationRelativeToNow = _lookupTtl;
            var baseQ = _db.Tasks.AsNoTracking().AsQueryable();
            if (teamId.HasValue) baseQ = baseQ.Where(t => t.TeamId == teamId);
            var names = await baseQ
                .Where(t => t.Property != null && t.Property != "")
                .Select(t => t.Property!)
                .Distinct()
                .OrderBy(p => p)
                .ToListAsync();
            return names.Select(n => new PropertyItem { Name = n }).ToList();
        }) ?? [];

        vm.WorkStates = await _cache.GetOrCreateAsync("lookup:workstates", async e => {
            e.AbsoluteExpirationRelativeToNow = _lookupTtl;
            var ws = await _db.WorkStates.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
            return ws.Any() ? ws : TaskListViewModel.AllStatuses
                .Select((s, i) => new WorkState { Id = i, Name = s, Color = "#094f70", SortOrder = i })
                .ToList();
        }) ?? [];

        var teamKey = teamId.HasValue ? $"lookup:owners:{teamId}" : "lookup:owners:global";
        vm.Owners = await _cache.GetOrCreateAsync(teamKey, async e => {
            e.AbsoluteExpirationRelativeToNow = _lookupTtl;
            IQueryable<AppUser> ownerQ = _db.AppUsers.AsNoTracking().OrderBy(u => u.DisplayName);
            if (teamId.HasValue)
            {
                var memberIds = await _db.TeamMembers.AsNoTracking()
                    .Where(m => m.TeamId == teamId).Select(m => m.UserId).ToListAsync();
                ownerQ = ownerQ.Where(u => memberIds.Contains(u.Id));
            }
            return (await ownerQ.ToListAsync())
                .Select(u => new OwnerItem { Id = u.Id, Name = u.DisplayName, Color = u.Color ?? "#094f70", ImagePath = u.ProfileImagePath })
                .ToList();
        }) ?? [];

        var sprintCacheKey = teamId.HasValue ? $"lookup:sprints:{teamId}" : "lookup:sprints:global";
        var allSprints = await _cache.GetOrCreateAsync(sprintCacheKey, async e => {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            var sprintQ = _db.Sprints.AsNoTracking().AsQueryable();
            if (teamId.HasValue) sprintQ = sprintQ.Where(s => s.TeamId == teamId);
            return await sprintQ.OrderBy(s => s.Name).ToListAsync();
        }) ?? [];

        TaskTeamViewData? teamViewData = null;
        if (f.View == "team")
            teamViewData = await GetTeamViewDataAsync(now, f.TlFrom, f.TlTo, f.TlMode, vm.CalYear, vm.CalMonth, teamId);

        return new TaskListResult { ViewModel = vm, AllSprints = allSprints, TeamViewData = teamViewData };
    }

    public async Task<TaskTeamViewData> GetTeamViewDataAsync(DateTime now, string? tlfrom, string? tlto,
        string? tlmode, int calYear, int calMonth, int? teamId)
    {
        var tlQuery = _db.Tasks.AsNoTracking()
            .Where(t => !t.IsDeleted)
            .AsQueryable();
        if (teamId.HasValue) tlQuery = tlQuery.Where(t => t.TeamId == teamId);
        var allForTl = await tlQuery.OrderBy(t => t.Id).ToListAsync();

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

        var appUsers    = await _db.AppUsers.OrderBy(u => u.DisplayName).ToListAsync();
        var regOwners   = appUsers.Select(u => u.DisplayName).Where(n => !string.IsNullOrEmpty(n)).ToList();
        var extraOwners = allForTl
            .Where(t => !string.IsNullOrEmpty(t.Owner))
            .SelectMany(t => t.Owner!.Split(',').Select(o => o.Trim()))
            .Where(o => o.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(o => !regOwners.Any(r => string.Equals(r, o, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var projects   = await _db.Projects.ToListAsync();
        var properties = await _db.Properties.ToListAsync();
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        return new TaskTeamViewData {
            TlFrom           = tlFrom.ToString("yyyy-MM-dd"),
            TlTo             = tlTo.ToString("yyyy-MM-dd"),
            TlMode           = resolvedMode,
            TlOwners         = JsonSerializer.Serialize(regOwners.Concat(extraOwners).ToList()),
            TlProjectColors  = JsonSerializer.Serialize(
                projects.Where(p => !string.IsNullOrEmpty(p.Color))
                        .ToDictionary(p => p.Name ?? "", p => p.Color ?? "#6b7280")),
            TlPropertyColors = JsonSerializer.Serialize(
                properties.Where(p => !string.IsNullOrEmpty(p.Color))
                          .ToDictionary(p => p.Name ?? "", p => p.Color ?? "#6b7280")),
            TlTasks = JsonSerializer.Serialize(allForTl.Select(t => new {
                id                 = t.Id,
                goal               = t.Goal     ?? "",
                owner              = t.Owner    ?? "",
                status             = t.Status   ?? "",
                priority           = t.Priority ?? "",
                itemType           = t.ItemType ?? "",
                project            = t.Project  ?? "",
                property           = t.Property ?? "",
                startDate          = t.StartDate?.ToString("yyyy-MM-dd") ?? "",
                endDate            = t.EndDate?.ToString("yyyy-MM-dd")   ?? "",
                weight             = t.Weight,
                swimlane           = t.Swimlane ?? "",
                reporter           = t.Reporter ?? "",
                createdBy          = t.CreatedBy ?? "",
                labels             = t.Labels   ?? "",
                notes              = t.Notes    ?? "",
                acceptanceCriteria = t.AcceptanceCriteria ?? "",
                parentId           = t.ParentId,
                sprintId           = t.SprintId,
            }), opts)
        };
    }

    public async Task<TaskItem?> GetByIdAsync(int id, int? teamId) => await FindTaskInTeam(id, teamId);

    public async Task<IEnumerable<object>> GetCommentsAsync(int taskId, int? teamId)
    {
        if (teamId.HasValue && !await _db.Tasks.AnyAsync(t => t.Id == taskId && t.TeamId == teamId))
            return [];
        var list = await _db.ActivityLog
            .Where(a => a.TaskId == taskId && a.Action == "Comment")
            .OrderBy(a => a.ChangedAt)
            .Select(a => new {
                a.ChangedBy, a.Details,
                changedAt    = a.ChangedAt.ToString("dd MMM yyyy HH:mm"),
                changedAtIso = a.ChangedAt.ToString("o")
            })
            .ToListAsync();
        return list.Cast<object>();
    }

    public async Task<IEnumerable<object>> GetHistoryAsync(int taskId, int? teamId)
    {
        if (teamId.HasValue && !await _db.Tasks.AnyAsync(t => t.Id == taskId && t.TeamId == teamId))
            return [];
        var list = await _db.ActivityLog
            .Where(a => a.TaskId == taskId && a.Action != "Comment")
            .OrderByDescending(a => a.ChangedAt)
            .Select(a => new {
                a.Action, a.Details, a.ChangedBy,
                changedAt    = a.ChangedAt.ToString("dd MMM yyyy HH:mm"),
                changedAtIso = a.ChangedAt.ToString("o")
            })
            .ToListAsync();
        return list.Cast<object>();
    }

    public async Task<object?> GetLinksAsync(int taskId, int? teamId)
    {
        if (teamId.HasValue && !await _db.Tasks.AnyAsync(t => t.Id == taskId && t.TeamId == teamId))
            return null;
        var links = await _db.TaskLinks
            .Where(l => l.FromTaskId == taskId || l.ToTaskId == taskId)
            .ToListAsync();
        var linkedIds = links.SelectMany(l => new[] { l.FromTaskId, l.ToTaskId })
            .Distinct().Where(x => x != taskId).ToList();
        var taskQuery = _db.Tasks.Where(t => linkedIds.Contains(t.Id));
        if (teamId.HasValue) taskQuery = taskQuery.Where(t => t.TeamId == teamId);
        var tasks = await taskQuery
            .Select(t => new { t.Id, t.Goal, t.Status, t.ItemType })
            .ToDictionaryAsync(t => t.Id);
        return links.Where(l => {
                var linkedId = l.FromTaskId == taskId ? l.ToTaskId : l.FromTaskId;
                return tasks.ContainsKey(linkedId);
            })
            .Select(l => {
            var isFrom   = l.FromTaskId == taskId;
            var linkedId = isFrom ? l.ToTaskId : l.FromTaskId;
            tasks.TryGetValue(linkedId, out var linked);
            return new {
                id = l.Id, linkType = l.LinkType, isFrom, linkedTaskId = linkedId,
                linkedTaskGoal     = linked?.Goal     ?? "Unknown",
                linkedTaskStatus   = linked?.Status   ?? "",
                linkedTaskItemType = linked?.ItemType ?? ""
            };
        }).ToList<object>();
    }

    public async Task<WatcherResult> GetWatchersAsync(int taskId, int? teamId, string displayName)
    {
        if (teamId.HasValue && !await _db.Tasks.AnyAsync(t => t.Id == taskId && t.TeamId == teamId))
            return new WatcherResult();
        var watchers = await _db.TaskWatchers
            .Where(w => w.TaskId == taskId)
            .Join(_db.AppUsers, w => w.UserId, u => u.Id, (w, u) => new { id = u.Id, name = u.DisplayName })
            .ToListAsync();
        return new WatcherResult {
            Watchers = watchers.Cast<object>().ToList(),
            Watching = watchers.Any(w => w.name == displayName),
            Count    = watchers.Count
        };
    }

    public async Task<IEnumerable<object>> GetAttachmentsAsync(int taskId)
    {
        var list = await _db.TaskAttachments
            .Where(a => a.TaskId == taskId)
            .OrderByDescending(a => a.UploadedAt)
            .Select(a => new {
                a.Id, a.FileName, a.FileSize, a.ContentType, a.UploadedBy,
                uploadedAt = a.UploadedAt.ToString("dd MMM yyyy HH:mm")
            })
            .ToListAsync();
        return list.Cast<object>();
    }

    public async Task<AttachmentDownload?> GetDownloadAsync(int id, int? teamId = null)
    {
        var att = await _db.TaskAttachments.FindAsync(id);
        if (att == null) return null;
        if (teamId.HasValue)
        {
            var parentTask = await _db.Tasks.FindAsync(att.TaskId);
            if (parentTask == null || parentTask.TeamId != teamId) return null;
        }
        var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "attachments",
            att.TaskId.ToString(), att.StoredName);
        if (!File.Exists(path)) return null;
        return new AttachmentDownload {
            FilePath    = path,
            ContentType = att.ContentType ?? "application/octet-stream",
            FileName    = att.FileName
        };
    }

    public async Task<ChildStatsResult?> GetChildStatsAsync(int id, int? teamId)
    {
        if (teamId.HasValue && !await _db.Tasks.AnyAsync(t => t.Id == id && t.TeamId == teamId))
            return null;

        // BFS level-by-level (max 6 depths) — avoids loading all team tasks
        var descendants = new List<TaskItem>();
        var parentIds   = new List<int> { id };
        for (int depth = 0; depth < 6 && parentIds.Count > 0; depth++)
        {
            var childrenQuery = _db.Tasks.AsNoTracking()
                .Where(t => t.ParentId.HasValue && parentIds.Contains(t.ParentId.Value))
                .AsQueryable();
            if (teamId.HasValue) childrenQuery = childrenQuery.Where(t => t.TeamId == teamId);
            var children = await childrenQuery.ToListAsync();
            descendants.AddRange(children);
            parentIds = children.Select(t => t.Id).ToList();
        }

        var leaves = descendants.Where(t => t.ItemType is "Task" or "Bug").ToList();
        if (leaves.Any())
        {
            var done = leaves.Count(t => TaskStatusRules.IsDone(t.ItemType, t.Status));
            return new ChildStatsResult {
                Total = leaves.Count, Done = done,
                Pct = (int)Math.Round(100.0 * done / leaves.Count), LeafType = "tasks"
            };
        }

        var direct = descendants.Where(t => t.ParentId == id).ToList();
        if (!direct.Any()) return new ChildStatsResult { LeafType = "items" };
        var directDone = direct.Count(t => TaskStatusRules.IsDone(t.ItemType, t.Status));
        return new ChildStatsResult {
            Total = direct.Count, Done = directDone,
            Pct = (int)Math.Round(100.0 * directDone / direct.Count), LeafType = "items"
        };
    }

    public async Task<IEnumerable<object>> GetSprintsAsync(int? teamId)
    {
        var raw = await TeamSprints(teamId)
            .OrderByDescending(s => s.IsActive).ThenByDescending(s => s.CreatedAt)
            .Select(s => new { s.Id, s.Name, s.IsActive, s.StartDate, s.EndDate })
            .ToListAsync();
        return raw.Select(s => (object)new {
            id        = s.Id,
            name      = s.Name,
            isActive  = s.IsActive,
            startDate = s.StartDate?.ToString("yyyy-MM-dd"),
            endDate   = s.EndDate?.ToString("yyyy-MM-dd")
        }).ToList();
    }

    public async Task<IEnumerable<object>> GetParentItemsAsync(string itemType, int? teamId)
    {
        var parentType = itemType switch {
            "Feature" => "Epic",
            "Story"   => "Feature",
            "Task"    => "Story",
            "Bug"     => "Story",
            _         => ""
        };
        if (string.IsNullOrEmpty(parentType)) return [];
        var q = _db.Tasks.Where(t => t.ItemType == parentType);
        if (teamId.HasValue) q = q.Where(t => t.TeamId == teamId);
        var list = await q.OrderBy(t => t.Goal)
            .Select(t => new {
                id = t.Id, goal = t.Goal, status = t.Status,
                moduleId = t.ModuleId, clientId = t.ClientId,
                project  = t.Project,  property = t.Property, priority = t.Priority
            }).ToListAsync();
        return list.Cast<object>();
    }

    public async Task<IEnumerable<object>> SearchAsync(string q, int? teamId)
    {
        if (string.IsNullOrWhiteSpace(q)) return [];
        IQueryable<TaskItem> query = _db.Tasks;
        if (teamId.HasValue) query = query.Where(t => t.TeamId == teamId);
        query = int.TryParse(q.Trim(), out var id)
            ? query.Where(t => t.Id == id || (t.Goal != null && t.Goal.Contains(q)))
            : query.Where(t => t.Goal != null && t.Goal.Contains(q));
        var list = await query.OrderBy(t => t.Goal).Take(10)
            .Select(t => new { id = t.Id, goal = t.Goal, status = t.Status, itemType = t.ItemType })
            .ToListAsync();
        return list.Cast<object>();
    }

    public async Task<IEnumerable<string>> GetLabelsAsync(int? teamId)
    {
        var raw = await TeamTasks(teamId)
            .Where(t => !string.IsNullOrEmpty(t.Labels) && !t.IsDeleted)
            .Select(t => t.Labels!)
            .ToListAsync();
        return raw
            .SelectMany(l => l.Split(',').Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0))
            .Distinct().OrderBy(l => l).ToList();
    }

    private static string? AddFastTimeout(string? cs) =>
        string.IsNullOrWhiteSpace(cs) ? cs : cs.TrimEnd(';') + ";Connect Timeout=4";

    public async Task<IEnumerable<object>> GetModulesAsync(string? himCs)
    {
        if (string.IsNullOrWhiteSpace(himCs)) return [];
        var list = new List<object>();
        try
        {
            await using var con = new SqlConnection(AddFastTimeout(himCs));
            await con.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT Id, Code, Name FROM Projects WHERE IsActive = 1 ORDER BY Name", con);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                list.Add(new { id = rdr.GetInt32(0), code = rdr.GetString(1), name = rdr.GetString(2) });
        }
        catch (Exception ex) { _log.LogWarning(ex, "GetModulesAsync: HIM DB unavailable"); }
        return list;
    }

    public async Task<IEnumerable<object>> GetClientsAsync(int moduleId, string? himCs, bool includeContactDetails = false)
    {
        if (string.IsNullOrWhiteSpace(himCs)) return [];
        var list = new List<object>();
        try
        {
            await using var con = new SqlConnection(AddFastTimeout(himCs));
            await con.OpenAsync();
            var sql = moduleId > 0
                ? @"SELECT DISTINCT cd.Id,cd.Name,cd.ContactPerson,cd.ContactNo01,cd.Email01,1 AS FromCompany
                    FROM Clients c JOIN CompanyDetails cd ON cd.Id=c.CompanyId
                    WHERE c.IsActive=1 AND NULLIF(NULLIF(cd.Name,''),'N/A') IS NOT NULL
                      AND EXISTS(SELECT 1 FROM ClientWiseProjects cwp WHERE cwp.ClientId=c.Id AND cwp.ProjectId=@mid)
                    UNION
                    SELECT c.Id,c.Name,c.ContactPerson,c.ContactNo01,c.Email01,0
                    FROM Clients c LEFT JOIN CompanyDetails cd ON cd.Id=c.CompanyId
                    WHERE c.IsActive=1 AND NULLIF(NULLIF(cd.Name,''),'N/A') IS NULL
                      AND EXISTS(SELECT 1 FROM ClientWiseProjects cwp WHERE cwp.ClientId=c.Id AND cwp.ProjectId=@mid)
                    ORDER BY Name"
                : @"SELECT DISTINCT cd.Id,cd.Name,cd.ContactPerson,cd.ContactNo01,cd.Email01,1 AS FromCompany
                    FROM Clients c JOIN CompanyDetails cd ON cd.Id=c.CompanyId
                    WHERE c.IsActive=1 AND NULLIF(NULLIF(cd.Name,''),'N/A') IS NOT NULL
                    UNION
                    SELECT c.Id,c.Name,c.ContactPerson,c.ContactNo01,c.Email01,0
                    FROM Clients c LEFT JOIN CompanyDetails cd ON cd.Id=c.CompanyId
                    WHERE c.IsActive=1 AND NULLIF(NULLIF(cd.Name,''),'N/A') IS NULL
                    ORDER BY Name";
            await using var cmd = new SqlCommand(sql, con);
            if (moduleId > 0) cmd.Parameters.AddWithValue("@mid", moduleId);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var id   = rdr.GetInt32(0);
                var name = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
                if (includeContactDetails)
                    list.Add(new {
                        id,
                        name,
                        contactPerson = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                        contactNo     = rdr.IsDBNull(3) ? "" : rdr.GetString(3),
                        email         = rdr.IsDBNull(4) ? "" : rdr.GetString(4),
                        fromCompany   = rdr.GetInt32(5) == 1
                    });
                else
                    list.Add(new { id, name });
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "GetClientsAsync: HIM DB unavailable for module {ModuleId}", moduleId); }
        return list;
    }

    public async Task<BacklogResult> GetBacklogAsync(string sprintFilter, int? teamId)
    {
        var query = _db.Tasks.AsNoTracking().AsQueryable();
        if (teamId.HasValue) query = query.Where(t => t.TeamId == teamId);

        if (sprintFilter == "active")
        {
            var spQ = _db.Sprints.AsQueryable();
            if (teamId.HasValue) spQ = spQ.Where(s => s.TeamId == teamId);
            var active = await spQ.FirstOrDefaultAsync(s => s.IsActive);
            query = active != null
                ? query.Where(t => t.SprintId == active.Id)
                : query.Where(t => t.SprintId == null);
        }
        else if (sprintFilter == "backlog")
            query = query.Where(t => t.SprintId == null);
        else if (int.TryParse(sprintFilter, out var sid) && sid > 0)
            query = query.Where(t => t.SprintId == sid);

        var tasks   = await query
            .OrderBy(t => t.BacklogOrder == null).ThenBy(t => t.BacklogOrder).ThenBy(t => t.CreatedAt)
            .ToListAsync();
        var sprints = await TeamSprints(teamId)
            .OrderByDescending(s => s.IsActive).ThenByDescending(s => s.CreatedAt)
            .ToListAsync();

        return new BacklogResult { Tasks = tasks, Sprints = sprints };
    }

    public async Task<BurndownResult?> GetBurndownAsync(int sprintId, int? teamId)
    {
        var sprint = await FindSprintInTeam(sprintId, teamId);
        if (sprint == null) return null;

        var snapshot = sprint.CompletedAt.HasValue
            ? await _db.SprintCompletionSnapshots.AsNoTracking()
                .FirstOrDefaultAsync(s => s.SprintId == sprintId)
            : null;
        if (snapshot != null)
        {
            var snapshotStart = sprint.StartDate?.Date ?? snapshot.CompletedAt.Date;
            var snapshotEnd = snapshot.CompletedAt.Date;
            if (snapshotEnd < snapshotStart) snapshotStart = snapshotEnd;
            var snapshotTotalDays = Math.Max(1, (snapshotEnd - snapshotStart).Days + 1);
            var snapshotDivisor = Math.Max(1, snapshotTotalDays - 1);
            var snapshotDays = new List<object>(); var snapshotIdeal = new List<object>();
            var snapshotWeightDays = new List<object>(); var snapshotWeightIdeal = new List<object>();
            var finalRemaining = snapshot.CommittedCount - snapshot.CompletedCount;
            var finalWeightRemaining = snapshot.CommittedEstimate - snapshot.CompletedEstimate;
            for (var i = 0; i < snapshotTotalDays; i++)
            {
                var day = snapshotStart.AddDays(i);
                var isFinal = day >= snapshotEnd;
                var actual = isFinal ? finalRemaining : snapshot.CommittedCount;
                var actualWeight = isFinal ? finalWeightRemaining : snapshot.CommittedEstimate;
                var idealRemaining = snapshot.CommittedCount - (snapshot.CommittedCount * i / (double)snapshotDivisor);
                var idealWeightRemaining = snapshot.CommittedEstimate - (snapshot.CommittedEstimate * i / (decimal)snapshotDivisor);
                snapshotDays.Add(new { date = day.ToString("MMM dd"), remaining = actual });
                snapshotIdeal.Add(new { date = day.ToString("MMM dd"), remaining = Math.Round(idealRemaining, 1) });
                snapshotWeightDays.Add(new { date = day.ToString("MMM dd"), remaining = Math.Round((double)actualWeight, 1) });
                snapshotWeightIdeal.Add(new { date = day.ToString("MMM dd"), remaining = Math.Round((double)idealWeightRemaining, 1) });
            }
            return new BurndownResult {
                SprintName = sprint.Name, Total = snapshot.CommittedCount,
                TotalWeight = snapshot.CommittedEstimate, Days = snapshotDays, Ideal = snapshotIdeal,
                WeightDays = snapshotWeightDays, WeightIdeal = snapshotWeightIdeal,
                IsHistoricalSnapshot = true, CarriedForwardCount = snapshot.CarriedForwardCount
            };
        }

        var taskQ = _db.Tasks.Where(t => t.SprintId == sprintId);
        if (teamId.HasValue) taskQ = taskQ.Where(t => t.TeamId == teamId);
        var tasks   = await taskQ.Select(t => new { t.Id, t.Weight }).ToListAsync();
        var taskIds = tasks.Select(t => t.Id).ToList();
        var total       = taskIds.Count;
        var totalWeight = tasks.Sum(t => (decimal)(t.Weight ?? 0));
        var weightMap   = tasks.ToDictionary(t => t.Id, t => (decimal)(t.Weight ?? 0));

        DateTime start = sprint.StartDate?.Date ?? DateTime.Today.AddDays(-14);
        DateTime end   = sprint.EndDate?.Date   ?? DateTime.Today;
        if (end < start) end = start.AddDays(14);

        var completions = await _db.ActivityLog
            .Where(a => a.TaskId.HasValue && taskIds.Contains(a.TaskId.Value)
                     && a.Action == "Status Changed"
                     && (a.Details.Contains("'Done'") || a.Details.Contains("'Resolved'") || a.Details.Contains("'Closed'")))
            .Select(a => new { a.TaskId, a.ChangedAt })
            .ToListAsync();

        var firstDone = completions
            .GroupBy(c => c.TaskId!.Value)
            .ToDictionary(g => g.Key, g => g.Min(c => c.ChangedAt).Date);

        var days = new List<object>(); var ideal = new List<object>();
        var weightDays = new List<object>(); var weightIdeal = new List<object>();
        int totalDays = Math.Max(1, (end - start).Days + 1);
        double divisor = totalDays - 1 < 1 ? 1 : totalDays - 1;
        for (int i = 0; i < totalDays; i++)
        {
            var day = start.AddDays(i);
            int doneSoFar = firstDone.Values.Count(d => d <= day);
            decimal weightDoneSoFar = firstDone
                .Where(kv => kv.Value <= day)
                .Sum(kv => weightMap.TryGetValue(kv.Key, out var w) ? w : 0m);
            double idealRemaining       = total       - (total       * i / divisor);
            double idealWeightRemaining = (double)totalWeight - ((double)totalWeight * i / divisor);
            days.Add(new        { date = day.ToString("MMM dd"), remaining = total - doneSoFar });
            ideal.Add(new       { date = day.ToString("MMM dd"), remaining = Math.Round(idealRemaining, 1) });
            weightDays.Add(new  { date = day.ToString("MMM dd"), remaining = Math.Round((double)(totalWeight - weightDoneSoFar), 1) });
            weightIdeal.Add(new { date = day.ToString("MMM dd"), remaining = Math.Round(idealWeightRemaining, 1) });
        }

        return new BurndownResult {
            SprintName  = sprint.Name,
            Total       = total,
            TotalWeight = totalWeight,
            Days        = days,
            Ideal       = ideal,
            WeightDays  = weightDays,
            WeightIdeal = weightIdeal
        };
    }

    public async Task<(byte[] Bytes, string FileName)> ExportCsvAsync(ExportFilters f, int? teamId)
    {
        var query = _db.Tasks.AsNoTracking().AsQueryable();
        if (teamId.HasValue) query = query.Where(t => t.TeamId == teamId);

        if (!string.IsNullOrEmpty(f.Search))
            query = query.Where(t => (t.Goal ?? "").Contains(f.Search) || (t.Property ?? "").Contains(f.Search) ||
                                     (t.Owner ?? "").Contains(f.Search) || (t.Project  ?? "").Contains(f.Search));
        if (!string.IsNullOrEmpty(f.Project))
            query = f.Project == "__none__" ? query.Where(t => t.Project == "") : query.Where(t => t.Project == f.Project);
        if (!string.IsNullOrEmpty(f.Property)) query = query.Where(t => t.Property == f.Property);
        if (!string.IsNullOrEmpty(f.Owner))    query = query.Where(t => (t.Owner ?? "").Contains(f.Owner));
        if (!string.IsNullOrEmpty(f.Status))
            query = f.Status == "__none__" ? query.Where(t => t.Status == "") : query.Where(t => t.Status == f.Status);
        if (!string.IsNullOrEmpty(f.Priority)) query = query.Where(t => t.Priority == f.Priority);

        var tasks = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();
        if (DateTime.TryParse(f.DateFrom, out var dfrom))
            tasks = tasks.Where(t => t.TargetDate.HasValue && t.TargetDate.Value.Date >= dfrom.Date).ToList();
        if (DateTime.TryParse(f.DateTo, out var dto))
            tasks = tasks.Where(t => t.TargetDate.HasValue && t.TargetDate.Value.Date <= dto.Date).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("Id,Goal,Project,Client,Priority,Owner,Status,TargetDate,ExpectedStartDate,ExpectedEndDate,ActualStartDate,ActualEndDate,Weight,RelatedFiles,Notes,CreatedAt");
        foreach (var t in tasks)
            sb.AppendLine($"{t.Id},{Csv(t.Goal)},{Csv(t.Project)},{Csv(t.Property)},{Csv(t.Priority)},{Csv(t.Owner)},{Csv(t.Status)},{Csv(t.TargetDate?.ToString("yyyy-MM-dd"))},{Csv(t.StartDate?.ToString("yyyy-MM-dd"))},{Csv(t.EndDate?.ToString("yyyy-MM-dd"))},{Csv(t.ActualStartDate?.ToString("yyyy-MM-dd"))},{Csv(t.ActualEndDate?.ToString("yyyy-MM-dd"))},{t.Weight},{Csv(t.RelatedFiles)},{Csv(t.Notes)},{t.CreatedAt:yyyy-MM-dd HH:mm}");

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return (bytes, $"tasks-{DateTime.Now:yyyyMMdd}.csv");
    }
}
