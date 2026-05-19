using CourtBooking.Data;
using CourtBooking.Models;
using CourtBooking.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Npgsql.EntityFrameworkCore.PostgreSQL;

var builder = WebApplication.CreateBuilder(args);

// Railway injects a PORT env var — bind to it so the app is reachable
var railwayPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(railwayPort))
    builder.WebHost.UseUrls($"http://0.0.0.0:{railwayPort}");

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
var isPostgres = connectionString.StartsWith("postgresql://")
              || connectionString.StartsWith("postgres://")
              || connectionString.Contains("Host=");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (isPostgres)
        options.UseNpgsql(connectionString)
               .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
    else
        options.UseSqlite(connectionString)
               .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequiredLength = 6;
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";
});

// ── External OAuth providers (Google & Facebook) ───────────────────────────
// Credentials live in environment variables on Railway (Auth__Google__ClientId etc.)
// If either key is absent the provider is simply skipped — no crash.
var googleClientId     = builder.Configuration["Auth:Google:ClientId"];
var googleClientSecret = builder.Configuration["Auth:Google:ClientSecret"];
var fbAppId            = builder.Configuration["Auth:Facebook:AppId"];
var fbAppSecret        = builder.Configuration["Auth:Facebook:AppSecret"];

if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    builder.Services.AddAuthentication()
        .AddGoogle(o =>
        {
            o.ClientId     = googleClientId;
            o.ClientSecret = googleClientSecret;
        });
}

if (!string.IsNullOrEmpty(fbAppId) && !string.IsNullOrEmpty(fbAppSecret))
{
    builder.Services.AddAuthentication()
        .AddFacebook(o =>
        {
            o.AppId     = fbAppId;
            o.AppSecret = fbAppSecret;
        });
}

builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<KeyGeneratorService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddHttpClient();                                 // for EmailService (Brevo HTTP API)
builder.Services.AddHostedService<SubscriptionReminderHostedService>();
builder.Services.AddControllersWithViews();

// ── Data Protection key persistence ───────────────────────────────────────
// Anti-forgery tokens, auth cookies, and password-reset tokens are signed
// with keys managed by the Data Protection stack. On Railway the default
// in-container directory is ephemeral, so every deploy invalidates every
// token and logs every user out. Persist the keys to the mounted volume
// (UPLOADS_ROOT, /data on Railway) so they survive container restarts.
// Falls back to the default location locally / in dev.
{
    var keysRoot = Environment.GetEnvironmentVariable("UPLOADS_ROOT");
    if (!string.IsNullOrWhiteSpace(keysRoot))
    {
        var keysDir = Path.Combine(keysRoot, "dp-keys");
        Directory.CreateDirectory(keysDir);
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
            .SetApplicationName("CourtBook");
    }
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Only redirect HTTPS locally — Railway terminates SSL at its own proxy
if (app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles();   // serves wwwroot (CSS, JS, bundled assets)

// On Railway, uploaded files (court photos, logos, payment proofs) live on a
// persistent volume mounted at UPLOADS_ROOT (e.g. /data).
// Locally they stay inside wwwroot/uploads as before.
var uploadsEnvRoot = Environment.GetEnvironmentVariable("UPLOADS_ROOT");
if (!string.IsNullOrEmpty(uploadsEnvRoot))
{
    var uploadsPhysPath = Path.Combine(uploadsEnvRoot, "uploads");
    Directory.CreateDirectory(uploadsPhysPath);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(uploadsPhysPath),
        RequestPath  = "/uploads"
    });
}

app.MapStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Seed roles and admin user on startup
using (var scope = app.Services.CreateScope())
{
    var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    db.Database.Migrate();

    foreach (var role in new[] { "Admin", "Customer" })
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));

    // ── One-time data fix: assign orphaned records (OwnerId = NULL) to the
    //    first admin. Handles courts/settings created before multi-tenant migration.
    var admins = await userManager.GetUsersInRoleAsync("Admin");
    if (admins.Count == 1)
    {
        // Only auto-assign when there is exactly one admin — unambiguous.
        var firstAdmin = admins[0];

        var orphanSettings = await db.FacilitySettings
            .Where(s => s.OwnerId == null).ToListAsync();
        foreach (var s in orphanSettings)
            s.OwnerId = firstAdmin.Id;

        var orphanCourts = await db.Courts
            .Where(c => c.OwnerId == null).ToListAsync();
        foreach (var c in orphanCourts)
            c.OwnerId = firstAdmin.Id;

        if (orphanSettings.Any() || orphanCourts.Any())
            await db.SaveChangesAsync();
    }
}

app.Run();
