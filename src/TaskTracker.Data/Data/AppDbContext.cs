using Microsoft.EntityFrameworkCore;
using TaskTracker.Models;

namespace TaskTracker.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem>     Tasks         { get; set; }
    public DbSet<EmailLog>     EmailLogs     { get; set; }
    public DbSet<ProjectItem>  Projects      { get; set; }
    public DbSet<PropertyItem> Properties    { get; set; }
    public DbSet<OwnerItem>    Owners        { get; set; }
    public DbSet<ActivityLog>  ActivityLog   { get; set; }
    public DbSet<AppUser>      AppUsers      { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<WorkState>    WorkStates    { get; set; }
    public DbSet<Sprint>       Sprints       { get; set; }
    public DbSet<TaskLink>       TaskLinks       { get; set; }
    public DbSet<TaskAttachment> TaskAttachments { get; set; }
    public DbSet<SavedQuery>      SavedQueries     { get; set; }
    public DbSet<SprintCapacity>  SprintCapacities { get; set; }
    public DbSet<TaskWatcher>     TaskWatchers     { get; set; }
    public DbSet<Team>            Teams            { get; set; }
    public DbSet<TeamMember>      TeamMembers      { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskItem>()
            .Property(t => t.Weight)
            .HasColumnType("decimal(18,2)");

        modelBuilder.Entity<TaskItem>().HasQueryFilter(t => !t.IsDeleted);

        // Self-referencing hierarchy (ParentId → Tasks.Id)
        modelBuilder.Entity<TaskItem>()
            .HasOne<TaskItem>()
            .WithMany()
            .HasForeignKey(t => t.ParentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes for new hierarchy columns
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.ItemType);
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.ParentId);
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.SprintId);

        // TaskLinks indexes
        modelBuilder.Entity<TaskLink>().HasIndex(l => l.FromTaskId);
        modelBuilder.Entity<TaskLink>().HasIndex(l => l.ToTaskId);

        // Indexes for common filter/sort columns
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.Owner);
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.Status);
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.Project);
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.Property);
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.Priority);
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.TargetDate);
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.StartDate);
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.CreatedAt);
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.CreatedBy);

        // SprintCapacity column types and index (must match snapshot)
        modelBuilder.Entity<SprintCapacity>()
            .Property(c => c.CapacityHours).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<SprintCapacity>()
            .Property(c => c.OwnerName).HasMaxLength(200);
        modelBuilder.Entity<SprintCapacity>()
            .HasIndex(c => c.SprintId);

        // TaskAttachments lookup by task
        modelBuilder.Entity<TaskAttachment>().HasIndex(a => a.TaskId);

        // ActivityLog lookup indexes
        modelBuilder.Entity<ActivityLog>().HasIndex(a => a.TaskId);
        modelBuilder.Entity<ActivityLog>().HasIndex(a => a.TeamId);
        modelBuilder.Entity<ActivityLog>().HasIndex(a => a.Category);
        modelBuilder.Entity<ActivityLog>().HasIndex(a => a.ChangedAt);

        // EmailLogs lookup indexes
        modelBuilder.Entity<EmailLog>().HasIndex(e => e.SentAt);
        modelBuilder.Entity<EmailLog>().HasIndex(e => e.ToEmail);
        modelBuilder.Entity<EmailLog>().HasIndex(e => e.IsSuccess);

        // Notifications lookup by user
        modelBuilder.Entity<Notification>().HasIndex(n => n.UserId);

        // Watchers: lookup by task, one watch per user per task
        modelBuilder.Entity<TaskWatcher>().HasIndex(w => w.TaskId);
        modelBuilder.Entity<TaskWatcher>().HasIndex(w => new { w.TaskId, w.UserId }).IsUnique();

        // AppUsers lookup by username
        modelBuilder.Entity<AppUser>().HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<AppUser>().HasIndex(u => u.DisplayName);

        // Teams
        modelBuilder.Entity<Team>().HasIndex(t => t.Name);
        modelBuilder.Entity<TeamMember>().HasIndex(m => m.TeamId);
        modelBuilder.Entity<TeamMember>().HasIndex(m => m.UserId);
        modelBuilder.Entity<TeamMember>().HasIndex(m => new { m.TeamId, m.UserId }).IsUnique();
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.TeamId);
        modelBuilder.Entity<Sprint>().HasIndex(s => s.TeamId);
    }
}
