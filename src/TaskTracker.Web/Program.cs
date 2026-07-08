using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TaskTracker.Data;
using TaskTracker.Hubs;
using TaskTracker.Models;
using TaskTracker.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddResponseCompression(opts => {
    opts.EnableForHttps = true;
    opts.Providers.Add<BrotliCompressionProvider>();
    opts.Providers.Add<GzipCompressionProvider>();
});
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
           .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
builder.Services.AddSingleton<IEmailService, SmtpEmailService>();
builder.Services.AddHttpClient();   // used for Google reCAPTCHA verification

builder.Services.AddScoped<IActivityService, ActivityService>();
builder.Services.AddScoped<IChartService, ChartService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IQueryService, QueryService>();
builder.Services.AddScoped<ISprintService, SprintService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITeamService, TeamService>();
builder.Services.AddScoped<ITaskService, TaskService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath            = "/Account/Login";
        options.LogoutPath           = "/Account/Logout";
        options.AccessDeniedPath     = "/Account/Login";
        options.ExpireTimeSpan       = TimeSpan.FromHours(8);
        options.SlidingExpiration    = true;
        options.Cookie.HttpOnly      = true;
        options.Cookie.SecurePolicy  = CookieSecurePolicy.Always;
        options.Cookie.SameSite      = SameSiteMode.Lax;
    });

builder.Services.AddAuthorization(opts =>
{
    opts.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

try
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
                    ALTER TABLE [AppUsers] ADD [RequestedTeamId] INT NULL;";
            await cmd2.ExecuteNonQueryAsync();
        }
    }
    catch { /* baseline migrations — non-critical if columns already exist */ }
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
                -- TeamId on Tasks (EXEC avoids parse-time column resolution error)
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Tasks' AND COLUMN_NAME='TeamId')
                BEGIN
                    ALTER TABLE [Tasks] ADD [TeamId] INT NULL;
                    EXEC('UPDATE [Tasks] SET [TeamId] = (SELECT TOP 1 [Id] FROM [Teams])');
                END
                -- TeamId on Sprints
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Sprints' AND COLUMN_NAME='TeamId')
                BEGIN
                    ALTER TABLE [Sprints] ADD [TeamId] INT NULL;
                    EXEC('UPDATE [Sprints] SET [TeamId] = (SELECT TOP 1 [Id] FROM [Teams])');
                END";
            await cmd3.ExecuteNonQueryAsync();
        }
    }
    catch { /* Teams migration — will retry on next start */ }
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
            new AppUser { Username = "sachithk",  Role = "Admin",  DisplayName = "SachithK" },
            new AppUser { Username = "stehanis",  Role = "Member", DisplayName = "Stehani"  },
            new AppUser { Username = "anuradhad", Role = "Admin",  DisplayName = "Anuradha" },
            new AppUser { Username = "piyumit",   Role = "Member", DisplayName = "Piyumi"   },
            new AppUser { Username = "sachithm",  Role = "Member", DisplayName = "SachithM" },
            new AppUser { Username = "chathunia", Role = "Member", DisplayName = "Chathuni" },
            new AppUser { Username = "kaviyas",   Role = "Member", DisplayName = "Kaviya"   },
        };
        foreach (var u in seeds)
            u.PasswordHash = hasher.HashPassword(u, "Password@123");
        db.AppUsers.AddRange(seeds);
        db.SaveChanges();
    }
}
} catch { /* startup DB init failed — app still starts, will error on DB queries */ }

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.MapHub<TaskHub>("/hubs/tasks");

app.Run();
