using Microsoft.EntityFrameworkCore;
using TaskTracker.Models;

namespace TaskTracker.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem>     Tasks         { get; set; }
    public DbSet<ProjectItem>  Projects      { get; set; }
    public DbSet<PropertyItem> Properties    { get; set; }
    public DbSet<OwnerItem>    Owners        { get; set; }
    public DbSet<ActivityLog>  ActivityLog   { get; set; }
    public DbSet<AppUser>      AppUsers      { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<WorkState>    WorkStates    { get; set; }
    public DbSet<Sprint>       Sprints       { get; set; }
    public DbSet<TaskLink>     TaskLinks     { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskItem>()
            .Property(t => t.Weight)
            .HasColumnType("decimal(18,2)");

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

        // ActivityLog lookup by task
        modelBuilder.Entity<ActivityLog>().HasIndex(a => a.TaskId);
        modelBuilder.Entity<ActivityLog>().HasIndex(a => a.ChangedAt);

        // Notifications lookup by user
        modelBuilder.Entity<Notification>().HasIndex(n => n.UserId);

        // AppUsers lookup by username
        modelBuilder.Entity<AppUser>().HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<AppUser>().HasIndex(u => u.DisplayName);
    }
}
