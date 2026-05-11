using CourtBooking.Data;
using CourtBooking.Models;
using CourtBooking.Services;
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
        options.UseNpgsql(connectionString);
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

builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<KeyGeneratorService>();
builder.Services.AddControllersWithViews();

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
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    foreach (var role in new[] { "Admin", "Customer" })
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));

    // Admin account is created via the /Trial/Start self-service registration flow.
}

app.Run();
