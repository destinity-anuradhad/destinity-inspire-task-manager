using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Hubs;
using TaskTracker.Models;
using TaskTracker.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSingleton<IEmailService, SmtpEmailService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath         = "/Account/Login";
        options.LogoutPath        = "/Account/Logout";
        options.AccessDeniedPath  = "/Account/Login";
        options.ExpireTimeSpan    = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

var app = builder.Build();

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
    await conn.CloseAsync();

    // Apply any pending migrations (only AddUserEmailNotifications on existing DBs)
    await db.Database.MigrateAsync();

    // Seed default users on a brand-new DB
    if (!db.AppUsers.Any())
    {
        var hasher = new PasswordHasher<AppUser>();
        var seeds = new[]
        {
            new AppUser { Username = "sachithk",  Role = "Admin",  DisplayName = "SachithK" },
            new AppUser { Username = "stehanis",  Role = "Admin",  DisplayName = "Stehani"  },
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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.MapHub<TaskHub>("/hubs/tasks");

app.Run();
