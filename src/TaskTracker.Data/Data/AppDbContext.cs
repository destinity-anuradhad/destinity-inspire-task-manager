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
    public DbSet<SprintHoursLog>  SprintHoursLogs  { get; set; }
    public DbSet<TaskOwner>       TaskOwners        { get; set; }
    public DbSet<ModuleItem>      Modules           { get; set; }
    public DbSet<ClientItem>      Clients           { get; set; }
    public DbSet<WorkflowDefinition> WorkflowDefinitions { get; set; }
    public DbSet<WorkflowState>      WorkflowStates      { get; set; }
    public DbSet<WorkflowTransition> WorkflowTransitions { get; set; }
    public DbSet<SprintWorkItemEvent> SprintWorkItemEvents { get; set; }
    public DbSet<SprintCompletionSnapshot> SprintCompletionSnapshots { get; set; }
    public DbSet<TimeEntry> TimeEntries { get; set; }
    public DbSet<UserTimeOff> UserTimeOff { get; set; }
    public DbSet<EmailOutboxMessage> EmailOutboxMessages { get; set; }
    public DbSet<ProjectRelease> ProjectReleases { get; set; }
    public DbSet<TaskChecklistItem> TaskChecklistItems { get; set; }
    public DbSet<TaskTemplate> TaskTemplates { get; set; }
    public DbSet<TaskTemplateChecklistItem> TaskTemplateChecklistItems { get; set; }
    public DbSet<RecurringTaskDefinition> RecurringTaskDefinitions { get; set; }
    public DbSet<RecurringTaskRun> RecurringTaskRuns { get; set; }
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions { get; set; }
    public DbSet<TaskCustomFieldValue> TaskCustomFieldValues { get; set; }
    public DbSet<ApiClient> ApiClients { get; set; }
    public DbSet<WebhookSubscription> WebhookSubscriptions { get; set; }
    public DbSet<WebhookDelivery> WebhookDeliveries { get; set; }
    public DbSet<ImportJob> ImportJobs { get; set; }
    public DbSet<ImportRow> ImportRows { get; set; }
    public DbSet<ApiIdempotencyRecord> ApiIdempotencyRecords { get; set; }
    public DbSet<AutomationRule> AutomationRules { get; set; }
    public DbSet<AutomationRuleRun> AutomationRuleRuns { get; set; }
    public DbSet<SlaPolicy> SlaPolicies { get; set; }
    public DbSet<ChatMessage>    ChatMessages    { get; set; }
    public DbSet<ChatAttachment> ChatAttachments { get; set; }
    public DbSet<ChatReaction>   ChatReactions   { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskItem>()
            .Property(t => t.Weight)
            .HasColumnType("decimal(18,6)");
        modelBuilder.Entity<TaskItem>()
            .Property(t => t.EstimateUnit).HasMaxLength(20).HasDefaultValue("Hours");

        modelBuilder.Entity<TaskItem>()
            .Property(t => t.ActualHours)
            .HasColumnType("decimal(18,2)");
        modelBuilder.Entity<TaskItem>()
            .Property(t => t.RemainingWorkHours)
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
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.WorkflowDefinitionId);
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.ProjectId);

        // TaskLinks indexes
        modelBuilder.Entity<TaskLink>().HasIndex(l => l.FromTaskId);
        modelBuilder.Entity<TaskLink>().HasIndex(l => l.ToTaskId);

        // Indexes for common filter/sort columns
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

        modelBuilder.Entity<EmailOutboxMessage>().Property(e => e.ToEmail).HasMaxLength(320);
        modelBuilder.Entity<EmailOutboxMessage>().Property(e => e.ToName).HasMaxLength(200);
        modelBuilder.Entity<EmailOutboxMessage>().Property(e => e.Subject).HasMaxLength(500);
        modelBuilder.Entity<EmailOutboxMessage>().Property(e => e.Status).HasMaxLength(30);
        modelBuilder.Entity<EmailOutboxMessage>().Property(e => e.LastError).HasMaxLength(4000);
        modelBuilder.Entity<EmailOutboxMessage>().Property(e => e.IdempotencyKey).HasMaxLength(200);
        modelBuilder.Entity<EmailOutboxMessage>().HasIndex(e => new { e.Status, e.AvailableAt });
        modelBuilder.Entity<EmailOutboxMessage>().HasIndex(e => e.IdempotencyKey).IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL");

        modelBuilder.Entity<ProjectItem>().Property(p => p.Name).HasMaxLength(200);
        modelBuilder.Entity<ProjectItem>().Property(p => p.Status).HasMaxLength(30).HasDefaultValue("Active");
        modelBuilder.Entity<ProjectItem>().Property(p => p.Description).HasMaxLength(2000);
        modelBuilder.Entity<ProjectItem>().HasIndex(p => new { p.TeamId, p.Status, p.Name });
        modelBuilder.Entity<ProjectItem>()
            .HasOne<Team>().WithMany().HasForeignKey(p => p.TeamId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<ProjectItem>()
            .HasOne<AppUser>().WithMany().HasForeignKey(p => p.LeadUserId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<ProjectRelease>().Property(r => r.Name).HasMaxLength(200);
        modelBuilder.Entity<ProjectRelease>().Property(r => r.Status).HasMaxLength(30).HasDefaultValue("Planned");
        modelBuilder.Entity<ProjectRelease>().Property(r => r.Notes).HasMaxLength(2000);
        modelBuilder.Entity<ProjectRelease>().HasIndex(r => new { r.ProjectId, r.Name }).IsUnique();
        modelBuilder.Entity<ProjectRelease>().HasIndex(r => new { r.ProjectId, r.Status, r.TargetDate });
        modelBuilder.Entity<ProjectRelease>()
            .HasOne<ProjectItem>().WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TaskChecklistItem>().Property(i => i.Text).HasMaxLength(500);
        modelBuilder.Entity<TaskChecklistItem>().HasIndex(i => new { i.TaskId, i.SortOrder });
        modelBuilder.Entity<TaskChecklistItem>()
            .HasOne<TaskItem>().WithMany().HasForeignKey(i => i.TaskId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<TaskTemplate>().Property(t => t.Name).HasMaxLength(200);
        modelBuilder.Entity<TaskTemplate>().Property(t => t.ItemType).HasMaxLength(50);
        modelBuilder.Entity<TaskTemplate>().Property(t => t.EstimateUnit).HasMaxLength(20).HasDefaultValue("Hours");
        modelBuilder.Entity<TaskTemplate>().Property(t => t.Estimate).HasColumnType("decimal(18,6)");
        modelBuilder.Entity<TaskTemplate>().Property(t => t.Notes).HasMaxLength(4000);
        modelBuilder.Entity<TaskTemplate>().Property(t => t.AcceptanceCriteria).HasMaxLength(4000);
        modelBuilder.Entity<TaskTemplate>().HasIndex(t => new { t.TeamId, t.Name }).IsUnique()
            .HasFilter("[TeamId] IS NOT NULL");
        modelBuilder.Entity<TaskTemplateChecklistItem>().Property(i => i.Text).HasMaxLength(500);
        modelBuilder.Entity<TaskTemplateChecklistItem>().HasIndex(i => new { i.TaskTemplateId, i.SortOrder });
        modelBuilder.Entity<TaskTemplateChecklistItem>()
            .HasOne(i => i.TaskTemplate).WithMany().HasForeignKey(i => i.TaskTemplateId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<RecurringTaskDefinition>().Property(d => d.Goal).HasMaxLength(500);
        modelBuilder.Entity<RecurringTaskDefinition>().Property(d => d.Frequency).HasMaxLength(20);
        modelBuilder.Entity<RecurringTaskDefinition>().HasIndex(d => new { d.IsActive, d.NextRunAt });
        modelBuilder.Entity<RecurringTaskDefinition>().HasIndex(d => new { d.TeamId, d.TaskTemplateId });
        modelBuilder.Entity<RecurringTaskDefinition>()
            .HasOne<TaskTemplate>().WithMany().HasForeignKey(d => d.TaskTemplateId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<RecurringTaskDefinition>()
            .HasOne<AppUser>().WithMany().HasForeignKey(d => d.OwnerUserId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<RecurringTaskDefinition>()
            .HasOne<Team>().WithMany().HasForeignKey(d => d.TeamId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<RecurringTaskRun>().HasIndex(r => new { r.RecurringTaskDefinitionId, r.ScheduledFor }).IsUnique();
        modelBuilder.Entity<RecurringTaskRun>()
            .HasOne<RecurringTaskDefinition>().WithMany().HasForeignKey(r => r.RecurringTaskDefinitionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<RecurringTaskRun>()
            .HasOne<TaskItem>().WithMany().HasForeignKey(r => r.TaskId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<CustomFieldDefinition>().Property(d => d.Name).HasMaxLength(100);
        modelBuilder.Entity<CustomFieldDefinition>().Property(d => d.FieldType).HasMaxLength(20);
        modelBuilder.Entity<CustomFieldDefinition>().HasIndex(d => new { d.TeamId, d.Name }).IsUnique();
        modelBuilder.Entity<TaskCustomFieldValue>().Property(v => v.Value).HasMaxLength(1000);
        modelBuilder.Entity<TaskCustomFieldValue>().HasIndex(v => new { v.TaskId, v.CustomFieldDefinitionId }).IsUnique();
        modelBuilder.Entity<TaskCustomFieldValue>()
            .HasOne<TaskItem>().WithMany().HasForeignKey(v => v.TaskId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<TaskCustomFieldValue>()
            .HasOne<CustomFieldDefinition>().WithMany().HasForeignKey(v => v.CustomFieldDefinitionId).OnDelete(DeleteBehavior.Restrict);

        // Notifications lookup by user
        modelBuilder.Entity<Notification>().HasIndex(n => n.UserId);

        // Watchers: lookup by task, one watch per user per task
        modelBuilder.Entity<TaskWatcher>().HasIndex(w => w.TaskId);
        modelBuilder.Entity<TaskWatcher>().HasIndex(w => new { w.TaskId, w.UserId }).IsUnique();

        // AppUsers lookup by username
        modelBuilder.Entity<AppUser>().HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<AppUser>().HasIndex(u => u.DisplayName);
        modelBuilder.Entity<AppUser>().Property(u => u.DailyCapacityHours).HasPrecision(5, 2);

        modelBuilder.Entity<ApiClient>().HasIndex(c => c.KeyHash).IsUnique();
        modelBuilder.Entity<ApiClient>().HasIndex(c => new { c.OwnerUserId, c.RevokedAt });
        modelBuilder.Entity<ApiClient>().HasIndex(c => new { c.TeamId, c.RevokedAt });

        modelBuilder.Entity<WebhookSubscription>().HasIndex(s => new { s.TeamId, s.IsActive });
        modelBuilder.Entity<WebhookDelivery>().HasIndex(d => new { d.Status, d.AvailableAt });
        modelBuilder.Entity<WebhookDelivery>().HasIndex(d => d.SubscriptionId);

        modelBuilder.Entity<ImportJob>().HasIndex(j => new { j.TeamId, j.Status });
        modelBuilder.Entity<ImportRow>().HasIndex(r => r.ImportJobId);
        modelBuilder.Entity<ImportRow>().HasIndex(r => r.ExternalKey);

        modelBuilder.Entity<ApiIdempotencyRecord>().HasIndex(r => new { r.ApiClientId, r.IdempotencyKey }).IsUnique();

        // Automation rules are looked up per (team, trigger) on every task write, so index that path.
        modelBuilder.Entity<AutomationRule>().HasIndex(r => new { r.TeamId, r.TriggerType, r.IsEnabled });
        modelBuilder.Entity<AutomationRuleRun>().HasIndex(r => r.RuleId);
        modelBuilder.Entity<AutomationRuleRun>().HasIndex(r => r.TaskId);

        modelBuilder.Entity<SlaPolicy>().HasIndex(p => new { p.TeamId, p.IsEnabled });

        // Teams
        modelBuilder.Entity<Team>().HasIndex(t => t.Name);
        modelBuilder.Entity<TeamMember>().HasIndex(m => m.TeamId);
        modelBuilder.Entity<TeamMember>().HasIndex(m => m.UserId);
        modelBuilder.Entity<TeamMember>().HasIndex(m => new { m.TeamId, m.UserId }).IsUnique();
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.TeamId);
        modelBuilder.Entity<Sprint>().HasIndex(s => s.TeamId);
        modelBuilder.Entity<Sprint>().HasIndex(s => s.CompletedAt);

        // Chat
        modelBuilder.Entity<ChatMessage>().HasIndex(m => new { m.TeamId, m.Id });
        modelBuilder.Entity<ChatAttachment>().HasIndex(a => a.MessageId);
        modelBuilder.Entity<ChatReaction>().HasIndex(r => r.MessageId);
        modelBuilder.Entity<ChatReaction>().HasIndex(r => new { r.MessageId, r.UserId, r.Emoji }).IsUnique();

        // Composite covering indexes — eliminate full-scan hot paths
        modelBuilder.Entity<TaskItem>().HasIndex(t => new { t.TeamId, t.IsDeleted, t.Status });
        modelBuilder.Entity<Notification>().HasIndex(n => new { n.UserId, n.IsRead });

        // SprintHoursLogs — one row per (sprint, task); upserted when hours are logged
        modelBuilder.Entity<SprintHoursLog>()
            .Property(l => l.Hours).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<SprintHoursLog>()
            .HasIndex(l => new { l.SprintId, l.TaskId }).IsUnique();
        modelBuilder.Entity<SprintHoursLog>()
            .HasIndex(l => l.SprintId);

        // TaskOwners — junction table replacing Tasks.Owner comma-string
        modelBuilder.Entity<TaskOwner>()
            .HasKey(o => new { o.TaskId, o.UserId });
        modelBuilder.Entity<TaskOwner>()
            .HasOne<TaskItem>()
            .WithMany()
            .HasForeignKey(o => o.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<TaskOwner>()
            .HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<TaskOwner>()
            .HasIndex(o => o.UserId);

        // ModuleItem / ClientItem — PMS lookup tables (IDs imported from PMS, not auto-generated)
        modelBuilder.Entity<ModuleItem>()
            .Property(m => m.Id).ValueGeneratedNever();
        modelBuilder.Entity<ModuleItem>()
            .Property(m => m.Name).HasMaxLength(200);
        modelBuilder.Entity<ClientItem>()
            .Property(c => c.Id).ValueGeneratedNever();
        modelBuilder.Entity<ClientItem>()
            .Property(c => c.Name).HasMaxLength(200);

        // Tasks.ModuleId / Tasks.ClientId reference the EXTERNAL HIM database (its Projects /
        // Clients tables), NOT the local Modules / Clients tables — so there is deliberately no
        // local foreign key. A FK here rejected every HIM-sourced module/client on save
        // (FK_Tasks_Modules_ModuleId / FK_Tasks_Clients_ClientId). The display names are cached
        // in Project / Property; we keep plain indexes on the IDs for filtering.
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.ModuleId);
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.ClientId);

        // SprintCapacity.UserId FK → AppUsers
        modelBuilder.Entity<SprintCapacity>()
            .HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SprintCapacity>()
            .HasIndex(c => new { c.SprintId, c.UserId }).IsUnique();

        // Tasks → Teams / Sprints FKs (SetNull — team/sprint deletion doesn't cascade)
        modelBuilder.Entity<TaskItem>()
            .HasOne<Team>()
            .WithMany()
            .HasForeignKey(t => t.TeamId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<TaskItem>()
            .HasOne<Sprint>()
            .WithMany()
            .HasForeignKey(t => t.SprintId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<TaskItem>()
            .HasOne<ProjectItem>()
            .WithMany()
            .HasForeignKey(t => t.ProjectId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // Workflows are immutable once published. Tasks retain the revision they
        // were created/migrated under so reporting and status semantics remain stable.
        modelBuilder.Entity<WorkflowDefinition>()
            .HasIndex(w => new { w.TeamId, w.ItemType, w.Version }).IsUnique();
        modelBuilder.Entity<WorkflowDefinition>()
            .HasIndex(w => new { w.TeamId, w.ItemType, w.IsPublished });
        modelBuilder.Entity<WorkflowDefinition>()
            .Property(w => w.ItemType).HasMaxLength(50);
        modelBuilder.Entity<WorkflowDefinition>()
            .Property(w => w.Name).HasMaxLength(200);
        modelBuilder.Entity<WorkflowState>()
            .HasIndex(s => new { s.WorkflowDefinitionId, s.Key }).IsUnique();
        modelBuilder.Entity<WorkflowState>()
            .HasIndex(s => new { s.WorkflowDefinitionId, s.Name }).IsUnique();
        modelBuilder.Entity<WorkflowState>().Property(s => s.Key).HasMaxLength(100);
        modelBuilder.Entity<WorkflowState>().Property(s => s.Name).HasMaxLength(100);
        modelBuilder.Entity<WorkflowState>().Property(s => s.Category).HasMaxLength(50);
        modelBuilder.Entity<WorkflowTransition>()
            .HasIndex(t => new { t.WorkflowDefinitionId, t.FromStateId, t.ToStateId }).IsUnique();
        modelBuilder.Entity<WorkflowDefinition>()
            .HasOne<Team>()
            .WithMany()
            .HasForeignKey(w => w.TeamId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<WorkflowState>()
            .HasOne<WorkflowDefinition>()
            .WithMany()
            .HasForeignKey(s => s.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SprintWorkItemEvent>()
            .Property(e => e.Estimate).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<SprintWorkItemEvent>().Property(e => e.EventType).HasMaxLength(50);
        modelBuilder.Entity<SprintWorkItemEvent>().HasIndex(e => new { e.TaskId, e.OccurredAt });
        modelBuilder.Entity<SprintWorkItemEvent>().HasIndex(e => new { e.FromSprintId, e.OccurredAt });
        modelBuilder.Entity<SprintWorkItemEvent>().HasIndex(e => new { e.ToSprintId, e.OccurredAt });
        modelBuilder.Entity<SprintCompletionSnapshot>()
            .Property(s => s.CommittedEstimate).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<SprintCompletionSnapshot>()
            .Property(s => s.CompletedEstimate).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<SprintCompletionSnapshot>().HasIndex(s => s.SprintId).IsUnique();
        modelBuilder.Entity<SprintWorkItemEvent>()
            .HasOne<TaskItem>().WithMany().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SprintWorkItemEvent>()
            .HasOne<Sprint>().WithMany().HasForeignKey(e => e.FromSprintId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SprintWorkItemEvent>()
            .HasOne<Sprint>().WithMany().HasForeignKey(e => e.ToSprintId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SprintCompletionSnapshot>()
            .HasOne<Sprint>().WithMany().HasForeignKey(s => s.SprintId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TimeEntry>().Property(e => e.Hours).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<TimeEntry>().Property(e => e.EntryType).HasMaxLength(50);
        modelBuilder.Entity<TimeEntry>().Property(e => e.Note).HasMaxLength(2000);
        modelBuilder.Entity<TimeEntry>().HasIndex(e => new { e.TaskId, e.WorkDate });
        modelBuilder.Entity<TimeEntry>().HasIndex(e => new { e.SprintId, e.WorkDate });
        modelBuilder.Entity<TimeEntry>().HasIndex(e => new { e.UserId, e.WorkDate });
        modelBuilder.Entity<TimeEntry>()
            .HasOne<TaskItem>().WithMany().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TimeEntry>()
            .HasOne<Sprint>().WithMany().HasForeignKey(e => e.SprintId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<TimeEntry>()
            .HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<UserTimeOff>().Property(l => l.Reason).HasMaxLength(500);
        modelBuilder.Entity<UserTimeOff>().HasIndex(l => new { l.UserId, l.StartDate, l.EndDate });
        modelBuilder.Entity<UserTimeOff>()
            .HasOne<AppUser>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<WorkflowTransition>()
            .HasOne<WorkflowDefinition>()
            .WithMany()
            .HasForeignKey(t => t.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<TaskItem>()
            .HasOne<WorkflowDefinition>()
            .WithMany()
            .HasForeignKey(t => t.WorkflowDefinitionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // Sprint → Team FK
        modelBuilder.Entity<Sprint>()
            .HasOne<Team>()
            .WithMany()
            .HasForeignKey(s => s.TeamId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // ActivityLog → Team FK (SetNull; log entries survive team deletion)
        modelBuilder.Entity<ActivityLog>()
            .HasOne<Team>()
            .WithMany()
            .HasForeignKey(a => a.TeamId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // SavedQuery → Team FK
        modelBuilder.Entity<SavedQuery>()
            .HasOne<Team>()
            .WithMany()
            .HasForeignKey(q => q.TeamId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // AppUser.RequestedTeamId → Teams FK
        modelBuilder.Entity<AppUser>()
            .HasOne<Team>()
            .WithMany()
            .HasForeignKey(u => u.RequestedTeamId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
