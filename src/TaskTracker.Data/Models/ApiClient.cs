using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("ApiClients")]
public class ApiClient
{
    public int Id { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(12)] public string KeyPrefix { get; set; } = "";
    [MaxLength(128)] public string KeyHash { get; set; } = "";
    [MaxLength(2000)] public string Scopes { get; set; } = "";
    public int OwnerUserId { get; set; }
    public int? TeamId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    [MaxLength(45)] public string? LastUsedIp { get; set; }
    // When set, the key only sees and changes tasks its owner is assigned to (TaskOwners).
    public bool OwnTasksOnly { get; set; }
}
