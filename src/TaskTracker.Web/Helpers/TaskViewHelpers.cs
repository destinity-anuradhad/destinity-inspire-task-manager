using System.Net;
using System.Text.Json;
using TaskTracker.Models;

namespace TaskTracker.Web.Helpers;

public static class TaskViewHelpers
{
    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string PriorityBadge(string? p) => p switch
    {
        "P0" => "bg-red-100 text-red-700 border border-red-200",
        "P1" => "bg-orange-100 text-orange-700 border border-orange-200",
        "P2" => "bg-yellow-100 text-yellow-700 border border-yellow-200",
        "P3" => "bg-gray-100 text-gray-600 border border-gray-200",
        _    => "bg-gray-100 text-gray-500 border border-gray-200"
    };

    public static string StatusBadge(string? s) => s switch
    {
        "Done"        => "bg-green-100 text-green-700 border border-green-200",
        "On track"    => "bg-blue-100 text-blue-700 border border-blue-200",
        "Delayed"     => "bg-red-100 text-red-700 border border-red-200",
        "On hold"     => "bg-gray-100 text-gray-600 border border-gray-200",
        "Testing"     => "bg-amber-100 text-amber-700 border border-amber-200",
        "Not Started" => "bg-purple-100 text-purple-700 border border-purple-200",
        _             => "bg-gray-100 text-gray-500 border border-gray-200"
    };

    public static bool IsOverdue(DateTime? expectedEnd, string? status)
    {
        if (status is "Done" or "On hold") return false;
        return expectedEnd.HasValue && expectedEnd.Value.Date < DateTime.Today;
    }

    public static IEnumerable<string> SplitOwners(string? owner) =>
        (owner ?? "").Split(',').Select(o => o.Trim()).Where(o => o.Length > 0);

    public static string WeightLabel(decimal? w) => w switch
    {
        null or 0 => "",
        _         => $"{w:0.##}h"
    };

    public static string AvatarEl(
        string name,
        Dictionary<string, string> colorMap,
        Dictionary<string, string> imageMap,
        string szCls = "w-5 h-5", string txCls = "text-[9px]")
    {
        var enc = WebUtility.HtmlEncode(name);
        if (imageMap.TryGetValue(name, out var img) && !string.IsNullOrEmpty(img))
            return $"""<img src="{img}" alt="{enc}" class="{szCls} rounded-full object-cover border border-white flex-shrink-0" title="{enc}" />""";
        var clr     = colorMap.GetValueOrDefault(name, "#64748b");
        var initial = name.Length > 0 ? name[0].ToString().ToUpper() : "?";
        return $"""<span class="inline-flex items-center justify-center {szCls} rounded-full {txCls} font-bold border border-white text-white flex-shrink-0" style="background:{clr}" title="{enc}">{initial}</span>""";
    }

    public static string TaskDataJson(TaskItem t) => JsonSerializer.Serialize(new {
        t.Id, t.Goal, t.Project, t.Property, t.Priority,
        t.Owner, t.Status, t.ItemType,
        targetDate      = t.TargetDate?.ToString("yyyy-MM-dd"),
        startDate       = t.StartDate?.ToString("yyyy-MM-dd"),
        endDate         = t.EndDate?.ToString("yyyy-MM-dd"),
        actualStartDate = t.ActualStartDate?.ToString("yyyy-MM-dd"),
        actualEndDate   = t.ActualEndDate?.ToString("yyyy-MM-dd"),
        t.Weight, t.Notes, t.Labels, t.AcceptanceCriteria,
        t.RelatedFiles, t.Reporter, t.CreatedBy,
        createdAt       = t.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
        t.ParentId, t.SprintId, t.ModuleId, t.ClientId, t.Swimlane
    }, _jsonOpts);
}
