using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("AppUsers")]
public class AppUser
{
    public int    Id               { get; set; }
    public string Username         { get; set; } = "";
    public string PasswordHash     { get; set; } = "";
    public string Role             { get; set; } = "Member";
    public string DisplayName      { get; set; } = "";
    public string? ProfileImagePath { get; set; }
    public string? Email             { get; set; }
    public bool   NotifyOnAssigned  { get; set; } = true;
    public bool   NotifyOnComment   { get; set; } = true;
    public bool   NotifyOnBlocked   { get; set; } = true;
    public DateTime CreatedAt       { get; set; } = DateTime.Now;
    public string? Color            { get; set; }
    public string? JobTitle         { get; set; }
    public string? Department       { get; set; }
    public decimal? DailyCapacityHours { get; set; }
    public string?  WorkingDays     { get; set; } = "Mon,Tue,Wed,Thu,Fri";
    public string   Status          { get; set; } = "Active";
    public int?     RequestedTeamId { get; set; }
}
