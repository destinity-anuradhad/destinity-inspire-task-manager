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
        options.LoginPath        = "/Account/Login";
        options.LogoutPath       = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan   = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Seed users and migrate new columns on startup
using (var scope = app.Services.CreateScope())
{
    var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();

    // Add new AppUser columns if they don't exist yet
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='Email')
                ALTER TABLE AppUsers ADD Email NVARCHAR(200) NULL;
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='NotifyOnComment')
                ALTER TABLE AppUsers ADD NotifyOnComment BIT NOT NULL DEFAULT 1;
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AppUsers' AND COLUMN_NAME='NotifyOnBlocked')
                ALTER TABLE AppUsers ADD NotifyOnBlocked BIT NOT NULL DEFAULT 1;";
        await cmd.ExecuteNonQueryAsync();
    }
    await conn.CloseAsync();

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
