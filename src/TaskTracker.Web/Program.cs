using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using TaskTracker.Data;
using TaskTracker.Hubs;
using TaskTracker.Models;
using TaskTracker.Services;
using TaskTracker.Services.Options;

var builder = WebApplication.CreateBuilder(args);

// Fail fast if secrets were not supplied via environment variables.
// Set these before starting the app:
//   ConnectionStrings__DefaultConnection   (full SQL Server connection string)
//   ConnectionStrings__HimConnection       (full HIM SQL Server connection string)
//   Email__Password                        (Gmail app password)
//   Recaptcha__SecretKey                   (Google reCAPTCHA server-side secret)
static void RequireSecret(IConfiguration cfg, string key)
{
    var val = cfg[key];
    if (string.IsNullOrWhiteSpace(val) || val == "PLACEHOLDER")
        throw new InvalidOperationException(
            $"Required secret '{key}' is missing. " +
            $"Set the environment variable '{key.Replace(":", "__")}' before starting the app.");
}
RequireSecret(builder.Configuration, "ConnectionStrings:DefaultConnection");
RequireSecret(builder.Configuration, "Email:Password");
RequireSecret(builder.Configuration, "Recaptcha:SecretKey");

builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddMemoryCache();
builder.Services.AddSignalR();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<RecaptchaOptions>(builder.Configuration.GetSection("Recaptcha"));
builder.Services.AddResponseCompression(opts => {
    opts.EnableForHttps = true;
    opts.Providers.Add<BrotliCompressionProvider>();
    opts.Providers.Add<GzipCompressionProvider>();
});
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));
builder.Services.AddSingleton<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IEmailQueue, EmailQueue>();
builder.Services.AddHostedService<EmailBackgroundService>();
builder.Services.AddHttpClient();   // used for Google reCAPTCHA verification
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddScoped<IActivityService, ActivityService>();
builder.Services.AddScoped<IChartService, ChartService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IQueryService, QueryService>();
builder.Services.AddScoped<ISprintService, SprintService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserSessionValidator, UserSessionValidator>();
builder.Services.AddScoped<ITeamService, TeamService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IWorkflowService, WorkflowService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<ITaskTemplateService, TaskTemplateService>();
builder.Services.AddScoped<IRecurringTaskService, RecurringTaskService>();
builder.Services.AddScoped<ICustomFieldService, CustomFieldService>();
builder.Services.AddScoped<RecurringTaskProcessor>();
builder.Services.AddHostedService<RecurringTaskBackgroundService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath            = "/Account/Login";
        options.LogoutPath           = "/Account/Logout";
        options.AccessDeniedPath     = "/Dashboard";
        options.ExpireTimeSpan       = TimeSpan.FromHours(8);
        options.SlidingExpiration    = true;
        options.Cookie.HttpOnly      = true;
        options.Cookie.SecurePolicy  = CookieSecurePolicy.Always;
        options.Cookie.SameSite      = SameSiteMode.Lax;
        options.Events.OnValidatePrincipal = async context =>
        {
            var validator = context.HttpContext.RequestServices
                .GetRequiredService<IUserSessionValidator>();
            if (await validator.IsValidAsync(context.Principal!)) return;

            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        };
    });

builder.Services.AddAuthorization(opts =>
{
    opts.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

// Legacy schema bootstrap is disabled in every environment. The historical migration
// chain contains destructive paths, so schema changes must be reviewed, scripted,
// backed up, and applied as a separate deployment step.
var runBootstrap = builder.Configuration.GetValue<bool>("Startup:RunSchemaBootstrap", defaultValue: false);
if (runBootstrap)
{
    throw new InvalidOperationException(
        "Startup:RunSchemaBootstrap is disabled. Use the approved non-destructive database upgrade procedure.");
}

try
{
if (runBootstrap)
{
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Baseline: if the DB was created before EF migrations were introduced, seed
    // __EFMigrationsHistory so MigrateAsync() doesn't try to recreate existing tables.
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='__EFMigrationsHistory')
            BEGIN
                CREATE TABLE [__EFMigrationsHistory] (
                    [MigrationId]    NVARCHAR(150) NOT NULL,
                    [ProductVersion] NVARCHAR(32)  NOT NULL,
                    CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
                );
            END
            -- Mark InitialCreate as already applied when the core tables exist
            IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='Tasks')
               AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] LIKE '%InitialCreate%')
            BEGIN
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20260630094913_InitialCreate', '10.0.9');
            END";
        await cmd.ExecuteNonQueryAsync();
    }
    // Ensure columns/tables added by hand-authored migrations always exist.
    // Each block is isolated so one failure doesn't block the next.
    try
    {
        using (var cmd2 = conn.CreateCommand())
        {
            cmd2.CommandText = @"
                -- BacklogOrder
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Tasks' AND COLUMN_NAME='BacklogOrder')
                    ALTER TABLE [Tasks] ADD [BacklogOrder] INT NULL;
                -- Soft-delete
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'Tasks') AND name=N'IsDeleted')
                    ALTER TABLE [Tasks] ADD [IsDeleted] BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'Tasks') AND name=N'DeletedAt')
                    ALTER TABLE [Tasks] ADD [DeletedAt] DATETIME2 NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'Tasks') AND name=N'DeletedBy')
                    ALTER TABLE [Tasks] ADD [DeletedBy] NVARCHAR(MAX) NULL;
                -- SavedQueries table
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='SavedQueries')
                BEGIN
                    CREATE TABLE [SavedQueries] (
                        [Id]          INT           NOT NULL IDENTITY(1,1) PRIMARY KEY,
                        [Name]        NVARCHAR(200) NOT NULL,
                        [Description] NVARCHAR(MAX) NULL,
                        [Filters]     NVARCHAR(MAX) NOT NULL DEFAULT '{}',
                        [IsShared]    BIT           NOT NULL DEFAULT 0,
                        [CreatedBy]   NVARCHAR(200) NULL,
                        [CreatedAt]   DATETIME2     NOT NULL DEFAULT GETDATE()
                    );
                    CREATE INDEX [IX_SavedQueries_CreatedBy] ON [SavedQueries] ([CreatedBy]);
                END
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'SavedQueries') AND name = N'TeamId')
                    ALTER TABLE [SavedQueries] ADD [TeamId] INT NULL;
                -- SprintCapacities table
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='SprintCapacities')
                BEGIN
                    CREATE TABLE [SprintCapacities] (
                        [Id]            INT           NOT NULL IDENTITY,
                        [SprintId]      INT           NOT NULL,
                        [OwnerName]     NVARCHAR(200) NOT NULL,
                        [CapacityHours] DECIMAL(18,2) NOT NULL DEFAULT 0,
                        CONSTRAINT [PK_SprintCapacities] PRIMARY KEY ([Id])
                    );
                    CREATE INDEX [IX_SprintCapacities_SprintId] ON [SprintCapacities] ([SprintId]);
                    CREATE UNIQUE INDEX [IX_SprintCapacities_Sprint_Owner] ON [SprintCapacities] ([SprintId], [OwnerName]);
                END
                -- TaskWatchers table
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='TaskWatchers')
                BEGIN
                    CREATE TABLE [TaskWatchers] (
                        [Id]        INT IDENTITY(1,1) NOT NULL,
                        [TaskId]    INT NOT NULL,
                        [UserId]    INT NOT NULL,
                        [CreatedAt] DATETIME2 NOT NULL,
                        CONSTRAINT [PK_TaskWatchers] PRIMARY KEY ([Id])
                    );
                    CREATE INDEX [IX_TaskWatchers_TaskId] ON [TaskWatchers] ([TaskId]);
                    CREATE UNIQUE INDEX [IX_TaskWatchers_TaskId_UserId] ON [TaskWatchers] ([TaskId], [UserId]);
                END
                -- Notifications.TaskId
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Notifications' AND COLUMN_NAME='TaskId')
                    ALTER TABLE [Notifications] ADD [TaskId] INT NULL;
                -- AppUser profile extensions
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='JobTitle')
                    ALTER TABLE [AppUsers] ADD [JobTitle] nvarchar(200) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='Department')
                    ALTER TABLE [AppUsers] ADD [Department] nvarchar(200) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='DailyCapacityHours')
                    ALTER TABLE [AppUsers] ADD [DailyCapacityHours] decimal(5,2) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='WorkingDays')
                    ALTER TABLE [AppUsers] ADD [WorkingDays] nvarchar(50) NULL;
                -- Registration approval
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='Status')
                    ALTER TABLE [AppUsers] ADD [Status] NVARCHAR(20) NOT NULL DEFAULT 'Active';
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='RequestedTeamId')
                    ALTER TABLE [AppUsers] ADD [RequestedTeamId] INT NULL;
                -- Forced password change on first login
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='MustChangePassword')
                    ALTER TABLE [AppUsers] ADD [MustChangePassword] BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Tasks') AND name = N'RowVersion')
                    ALTER TABLE [Tasks] ADD [RowVersion] ROWVERSION NOT NULL;
                -- ActivityLog system fields (migrations 20260707190000 / 20260707200000)
                ALTER TABLE [ActivityLog] ALTER COLUMN [TaskId] INT NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ActivityLog') AND name = 'TeamId')
                    ALTER TABLE [ActivityLog] ADD [TeamId] INT NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ActivityLog') AND name = 'Category')
                    ALTER TABLE [ActivityLog] ADD [Category] NVARCHAR(50) NOT NULL DEFAULT 'Task';
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ActivityLog') AND name = 'IpAddress')
                    ALTER TABLE [ActivityLog] ADD [IpAddress] NVARCHAR(45) NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ActivityLog') AND name = 'UserAgent')
                    ALTER TABLE [ActivityLog] ADD [UserAgent] NVARCHAR(512) NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('ActivityLog') AND name = 'IX_ActivityLog_TeamId')
                    CREATE INDEX [IX_ActivityLog_TeamId] ON [ActivityLog] ([TeamId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('ActivityLog') AND name = 'IX_ActivityLog_Category')
                    CREATE INDEX [IX_ActivityLog_Category] ON [ActivityLog] ([Category]);
"; // date conversions handled below in per-column commands
            await cmd2.ExecuteNonQueryAsync();
        }
    }
    catch (Exception ex) { app.Logger.LogWarning(ex, "Baseline column migrations skipped (columns may already exist)"); }
    // Teams feature — isolated so it runs even if earlier migrations partially failed
    try
    {
        using (var cmd3 = conn.CreateCommand())
        {
            cmd3.CommandText = @"
                -- Teams
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='Teams')
                BEGIN
                    CREATE TABLE [Teams] (
                        [Id]          INT NOT NULL IDENTITY(1,1),
                        [Name]        NVARCHAR(200) NOT NULL,
                        [Description] NVARCHAR(MAX) NULL,
                        [Color]       NVARCHAR(50)  NOT NULL DEFAULT '#094f70',
                        [IsActive]    BIT           NOT NULL DEFAULT 1,
                        [CreatedAt]   DATETIME2     NOT NULL DEFAULT GETDATE(),
                        [CreatedBy]   NVARCHAR(200) NULL,
                        CONSTRAINT [PK_Teams] PRIMARY KEY ([Id])
                    );
                    CREATE INDEX [IX_Teams_Name] ON [Teams] ([Name]);
                    INSERT INTO [Teams] ([Name],[Description],[Color],[IsActive],[CreatedAt])
                    VALUES ('Destinity Inspire','Default team','#094f70',1,GETDATE());
                END
                -- TeamMembers
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='TeamMembers')
                BEGIN
                    CREATE TABLE [TeamMembers] (
                        [Id]       INT NOT NULL IDENTITY(1,1),
                        [TeamId]   INT NOT NULL,
                        [UserId]   INT NOT NULL,
                        [TeamRole] NVARCHAR(50)  NOT NULL DEFAULT 'Member',
                        [JoinedAt] DATETIME2     NOT NULL DEFAULT GETDATE(),
                        CONSTRAINT [PK_TeamMembers] PRIMARY KEY ([Id])
                    );
                    CREATE INDEX [IX_TeamMembers_TeamId] ON [TeamMembers] ([TeamId]);
                    CREATE INDEX [IX_TeamMembers_UserId] ON [TeamMembers] ([UserId]);
                    CREATE UNIQUE INDEX [IX_TeamMembers_TeamId_UserId] ON [TeamMembers] ([TeamId],[UserId]);
                    INSERT INTO [TeamMembers] ([TeamId],[UserId],[TeamRole],[JoinedAt])
                    SELECT (SELECT TOP 1 [Id] FROM [Teams]),
                           [Id],
                           CASE WHEN [Role]='Admin' THEN 'TeamAdmin' ELSE 'Member' END,
                           GETDATE()
                    FROM [AppUsers];
                END
                -- TeamId on Tasks and Sprints is now handled by EF migration
                -- 20260708100000_AddTeamIdToTasksAndSprints";
            await cmd3.ExecuteNonQueryAsync();
        }
    }
    catch (Exception ex) { app.Logger.LogWarning(ex, "Teams migration skipped (tables may already exist — will retry on next start)"); }

    // Date column conversions: each column in its own command so DECLARE is in a fresh batch
    // and a DEFAULT constraint on one column doesn't block the others.
    foreach (var (col, idxName) in new (string col, string? idx)[] {
        ("TargetDate",     "IX_Tasks_TargetDate"),
        ("StartDate",      "IX_Tasks_StartDate"),
        ("EndDate",        null),
        ("ActualStartDate", null),
        ("ActualEndDate",  null),
    })
    {
        try
        {
            using var cmdDate = conn.CreateCommand();
            cmdDate.CommandText = $@"
                IF EXISTS (
                    SELECT 1 FROM sys.columns c
                    JOIN sys.types t ON c.user_type_id = t.user_type_id
                    WHERE c.object_id = OBJECT_ID(N'Tasks') AND c.name = N'{col}'
                      AND t.name IN ('nvarchar','varchar','nchar','char')
                )
                BEGIN
                    DECLARE @dc NVARCHAR(200);
                    SELECT @dc = dc.name
                    FROM sys.default_constraints dc
                    JOIN sys.columns c2 ON dc.parent_object_id = c2.object_id
                                       AND dc.parent_column_id = c2.column_id
                    WHERE c2.object_id = OBJECT_ID(N'Tasks') AND c2.name = N'{col}';
                    IF @dc IS NOT NULL
                        EXEC('ALTER TABLE [Tasks] DROP CONSTRAINT [' + @dc + ']');
                    {(idxName != null ? $"IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'Tasks') AND name=N'{idxName}') DROP INDEX [{idxName}] ON [Tasks];" : "")}
                    ALTER TABLE [Tasks] ADD [{col}_new] DATETIME2 NULL;
                    UPDATE [Tasks] SET [{col}_new] = TRY_CONVERT(DATETIME2, [{col}], 23)
                        WHERE [{col}] IS NOT NULL AND [{col}] != '';
                    ALTER TABLE [Tasks] DROP COLUMN [{col}];
                    EXEC sp_rename N'Tasks.{col}_new', N'{col}', N'COLUMN';
                    {(idxName != null ? $"CREATE INDEX [{idxName}] ON [Tasks] ([{col}]);" : "")}
                END";
            await cmdDate.ExecuteNonQueryAsync();
        }
        catch (Exception ex) { app.Logger.LogWarning(ex, "Date column {Col} conversion failed", col); }
    }

    await conn.CloseAsync();

    // Apply any pending EF Core migrations
    try { await db.Database.MigrateAsync(); }
    catch (Exception ex) { app.Logger.LogError(ex, "EF Core migration failed — database may be in an inconsistent state"); }

    // Seed default users on a brand-new DB
    if (!db.AppUsers.Any())
    {
        var hasher = new PasswordHasher<AppUser>();
        var seeds = new[]
        {
            new AppUser { Username = "sachithk",  Role = "Admin",  DisplayName = "SachithK",  MustChangePassword = true },
            new AppUser { Username = "stehanis",  Role = "Member", DisplayName = "Stehani",   MustChangePassword = true },
            new AppUser { Username = "anuradhad", Role = "Admin",  DisplayName = "Anuradha",  MustChangePassword = true },
            new AppUser { Username = "piyumit",   Role = "Member", DisplayName = "Piyumi",    MustChangePassword = true },
            new AppUser { Username = "sachithm",  Role = "Member", DisplayName = "SachithM",  MustChangePassword = true },
            new AppUser { Username = "chathunia", Role = "Member", DisplayName = "Chathuni",  MustChangePassword = true },
            new AppUser { Username = "kaviyas",   Role = "Member", DisplayName = "Kaviya",    MustChangePassword = true },
        };
        foreach (var u in seeds)
            u.PasswordHash = hasher.HashPassword(u, "Password@123");
        db.AppUsers.AddRange(seeds);
        db.SaveChanges();
    }
}
} // end if (runBootstrap)
} catch (Exception ex) { app.Logger.LogCritical(ex, "Startup DB initialisation failed — app will start but DB queries will error"); }

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseStaticFiles();

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Redirect any authenticated user with MustChangePassword claim to the forced-change page.
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true &&
        ctx.User.FindFirst("MustChangePassword")?.Value == "1")
    {
        var path = ctx.Request.Path.Value ?? "";
        var skip = path.StartsWith("/Account/ForceChangePassword", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/Account/Logout", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase);
        if (!skip)
        {
            ctx.Response.Redirect("/Account/ForceChangePassword");
            return;
        }
    }
    await next();
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.MapHub<TaskHub>("/hubs/tasks");

app.Run();
