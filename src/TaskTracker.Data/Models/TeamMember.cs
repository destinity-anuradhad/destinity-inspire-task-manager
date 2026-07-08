using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

// TeamRole values: "TeamAdmin" | "Lead" | "Member"
[Table("TeamMembers")]
public class TeamMember
{
    public int      Id       { get; set; }
    public int      TeamId   { get; set; }
    public int      UserId   { get; set; }
    public string   TeamRole { get; set; } = "Member";
    public DateTime JoinedAt { get; set; } = DateTime.Now;
}
