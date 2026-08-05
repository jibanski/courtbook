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

// Load developer-only / machine-local overrides (Dev:Password, etc.) that
// shouldn't be checked in. ASP.NET Core does not pick up *.local.json files
// by default, so we register them explicitly. File is optional so production
// (Railway) keeps working without it.
builder.Configuration
    .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.local.json",
                 optional: true, reloadOnChange: true);

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
builder.Services.AddScoped<PayMongoService>();
builder.Services.AddScoped<KeyGeneratorService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<GuestCheckoutService>();
builder.Services.AddHttpClient();                                 // for EmailService (Brevo HTTP API)
builder.Services.AddHostedService<SubscriptionReminderHostedService>();
builder.Services.AddHostedService<ReservationExpiryCleanupService>();
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

    // ── Ensure new columns exist (fallback when migrations aren't discovered) ─
    try
    {
        if (isPostgres)
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Bookings\" ADD COLUMN IF NOT EXISTS \"CheckoutSessionId\" character varying(100) NULL");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"FacilitySettings\" ADD COLUMN IF NOT EXISTS \"PayMongoSecretKey\" character varying(100) NULL");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"FacilitySettings\" ADD COLUMN IF NOT EXISTS \"PayMongoMethods\" character varying(200) NULL DEFAULT 'qrph'");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"FacilitySettings\" ADD COLUMN IF NOT EXISTS \"GCashQrCodePath\" character varying(300) NULL");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"FacilitySettings\" ADD COLUMN IF NOT EXISTS \"MayaQrCodePath\" character varying(300) NULL");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"FacilitySettings\" ADD COLUMN IF NOT EXISTS \"GoTymeNumber\" character varying(20) NULL");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"FacilitySettings\" ADD COLUMN IF NOT EXISTS \"GoTymeName\" character varying(100) NULL");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"FacilitySettings\" ADD COLUMN IF NOT EXISTS \"GoTymeQrCodePath\" character varying(300) NULL");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"PlatformConfig\" ADD COLUMN IF NOT EXISTS \"LogoData\" bytea NULL");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"PlatformConfig\" ADD COLUMN IF NOT EXISTS \"LogoContentType\" character varying(50) NULL");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Bookings\" ADD COLUMN IF NOT EXISTS \"FacilityName\" character varying(100) NULL");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Courts\" ADD COLUMN IF NOT EXISTS \"FacilityName\" character varying(100) NULL");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"FacilitySettings\" ADD COLUMN IF NOT EXISTS \"IsDeactivated\" boolean NOT NULL DEFAULT FALSE");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"FacilitySettings\" ADD COLUMN IF NOT EXISTS \"DeactivatedAt\" timestamp with time zone NULL");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"FacilitySettings\" ADD COLUMN IF NOT EXISTS \"HouseRules\" text NULL");

            // Bump any rows that still have the old multi-method default to QRPh-only.
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"FacilitySettings\" SET \"PayMongoMethods\" = 'qrph' " +
                "WHERE \"PayMongoMethods\" = 'card,gcash,paymaya,grab_pay,qrph,dob'");

            // Backfill facility name onto existing bookings from the court owner's facility.
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"Bookings\" SET \"FacilityName\" = fs.\"FacilityName\" " +
                "FROM \"Courts\" c JOIN \"FacilitySettings\" fs ON fs.\"OwnerId\" = c.\"OwnerId\" " +
                "WHERE c.\"Id\" = \"Bookings\".\"CourtId\" AND \"Bookings\".\"FacilityName\" IS NULL");

            // Backfill facility name onto existing courts from the owner's facility.
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"Courts\" SET \"FacilityName\" = fs.\"FacilityName\" " +
                "FROM \"FacilitySettings\" fs WHERE fs.\"OwnerId\" = \"Courts\".\"OwnerId\" " +
                "AND \"Courts\".\"FacilityName\" IS NULL");
        }
        else
        {
            // SQLite: ADD COLUMN IF NOT EXISTS isn't supported; ignore errors
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Bookings\" ADD COLUMN \"CheckoutSessionId\" TEXT NULL"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"FacilitySettings\" ADD COLUMN \"PayMongoSecretKey\" TEXT NULL"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"FacilitySettings\" ADD COLUMN \"PayMongoMethods\" TEXT NULL DEFAULT 'qrph'"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"FacilitySettings\" ADD COLUMN \"GCashQrCodePath\" TEXT NULL"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"FacilitySettings\" ADD COLUMN \"MayaQrCodePath\" TEXT NULL"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"FacilitySettings\" ADD COLUMN \"GoTymeNumber\" TEXT NULL"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"FacilitySettings\" ADD COLUMN \"GoTymeName\" TEXT NULL"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"FacilitySettings\" ADD COLUMN \"GoTymeQrCodePath\" TEXT NULL"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"PlatformConfig\" ADD COLUMN \"LogoData\" BLOB NULL"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"PlatformConfig\" ADD COLUMN \"LogoContentType\" TEXT NULL"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Bookings\" ADD COLUMN \"FacilityName\" TEXT NULL"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Courts\" ADD COLUMN \"FacilityName\" TEXT NULL"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"FacilitySettings\" ADD COLUMN \"IsDeactivated\" INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"FacilitySettings\" ADD COLUMN \"DeactivatedAt\" TEXT NULL"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"FacilitySettings\" ADD COLUMN \"HouseRules\" TEXT NULL"); } catch { }

            // Bump any rows that still have the old multi-method default to QRPh-only.
            try {
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE \"FacilitySettings\" SET \"PayMongoMethods\" = 'qrph' " +
                    "WHERE \"PayMongoMethods\" = 'card,gcash,paymaya,grab_pay,qrph,dob'");
            } catch { }

            // Backfill facility name onto existing bookings from the court owner's facility.
            try {
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE \"Bookings\" SET \"FacilityName\" = (" +
                    "SELECT fs.\"FacilityName\" FROM \"Courts\" c " +
                    "JOIN \"FacilitySettings\" fs ON fs.\"OwnerId\" = c.\"OwnerId\" " +
                    "WHERE c.\"Id\" = \"Bookings\".\"CourtId\") " +
                    "WHERE \"FacilityName\" IS NULL");
            } catch { }

            // Backfill facility name onto existing courts from the owner's facility.
            try {
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE \"Courts\" SET \"FacilityName\" = (" +
                    "SELECT fs.\"FacilityName\" FROM \"FacilitySettings\" fs " +
                    "WHERE fs.\"OwnerId\" = \"Courts\".\"OwnerId\") " +
                    "WHERE \"FacilityName\" IS NULL");
            } catch { }
        }
    }
    catch { /* columns already exist — no-op */ }

    // ── Ensure CourtBlocks table exists ──────────────────────────────────
    // Added after the initial schema; created via raw SQL so no migration
    // file is required (project has no committed migrations).
    try
    {
        if (isPostgres)
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""CourtBlocks"" (
                    ""Id""        serial                      PRIMARY KEY,
                    ""CourtId""   integer                     NOT NULL,
                    ""StartDate"" date                        NOT NULL,
                    ""StartHour"" integer                     NOT NULL,
                    ""EndDate""   date                        NOT NULL,
                    ""EndHour""   integer                     NOT NULL,
                    ""Reason""    character varying(300)      NULL,
                    ""CreatedAt"" timestamp with time zone    NOT NULL DEFAULT NOW(),
                    CONSTRAINT ""FK_CourtBlocks_Courts_CourtId""
                        FOREIGN KEY (""CourtId"") REFERENCES ""Courts"" (""Id"") ON DELETE CASCADE
                )
            ");
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""CourtBlocks"" (
                    ""Id""        INTEGER  PRIMARY KEY AUTOINCREMENT,
                    ""CourtId""   INTEGER  NOT NULL,
                    ""StartDate"" TEXT     NOT NULL,
                    ""StartHour"" INTEGER  NOT NULL,
                    ""EndDate""   TEXT     NOT NULL,
                    ""EndHour""   INTEGER  NOT NULL,
                    ""Reason""    TEXT     NULL,
                    ""CreatedAt"" TEXT     NOT NULL DEFAULT (datetime('now')),
                    FOREIGN KEY (""CourtId"") REFERENCES ""Courts"" (""Id"") ON DELETE CASCADE
                )
            ");
        }
    }
    catch { /* table already exists or db not ready — non-fatal */ }

    // ── Ensure CourtRateTiers / CourtScheduleBlocks / FacilityHolidays tables exist ──
    // Facility owner default booking schedule & tiered rates — created via raw SQL
    // so no migration file is required (project has no committed migrations).
    try
    {
        if (isPostgres)
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""CourtRateTiers"" (
                    ""Id""              serial                  PRIMARY KEY,
                    ""CourtId""         integer                 NOT NULL,
                    ""DaysOfWeek""      character varying(40)   NOT NULL DEFAULT '',
                    ""IncludeHolidays"" boolean                 NOT NULL DEFAULT FALSE,
                    ""StartHour""       integer                 NOT NULL,
                    ""EndHour""         integer                 NOT NULL,
                    ""PricePerHour""    numeric(10,2)           NOT NULL DEFAULT 0,
                    CONSTRAINT ""FK_CourtRateTiers_Courts_CourtId""
                        FOREIGN KEY (""CourtId"") REFERENCES ""Courts"" (""Id"") ON DELETE CASCADE
                )
            ");
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""CourtScheduleBlocks"" (
                    ""Id""              serial                  PRIMARY KEY,
                    ""CourtId""         integer                 NOT NULL,
                    ""DaysOfWeek""      character varying(40)   NOT NULL DEFAULT '',
                    ""IncludeHolidays"" boolean                 NOT NULL DEFAULT FALSE,
                    ""StartHour""       integer                 NOT NULL,
                    ""EndHour""         integer                 NOT NULL,
                    ""Type""            integer                 NOT NULL DEFAULT 0,
                    ""IsActive""        boolean                 NOT NULL DEFAULT TRUE,
                    ""Description""     character varying(200)  NULL,
                    CONSTRAINT ""FK_CourtScheduleBlocks_Courts_CourtId""
                        FOREIGN KEY (""CourtId"") REFERENCES ""Courts"" (""Id"") ON DELETE CASCADE
                )
            ");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"CourtScheduleBlocks\" ADD COLUMN IF NOT EXISTS \"IsActive\" boolean NOT NULL DEFAULT TRUE");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"CourtScheduleBlocks\" ADD COLUMN IF NOT EXISTS \"Description\" character varying(200) NULL");
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""FacilityHolidays"" (
                    ""Id""      serial                  PRIMARY KEY,
                    ""OwnerId"" character varying(450)  NOT NULL,
                    ""Date""    date                    NOT NULL,
                    ""Label""   character varying(100)  NULL
                )
            ");
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""CourtRateTiers"" (
                    ""Id""              INTEGER  PRIMARY KEY AUTOINCREMENT,
                    ""CourtId""         INTEGER  NOT NULL,
                    ""DaysOfWeek""      TEXT     NOT NULL DEFAULT '',
                    ""IncludeHolidays"" INTEGER  NOT NULL DEFAULT 0,
                    ""StartHour""       INTEGER  NOT NULL,
                    ""EndHour""         INTEGER  NOT NULL,
                    ""PricePerHour""    TEXT     NOT NULL DEFAULT '0',
                    FOREIGN KEY (""CourtId"") REFERENCES ""Courts"" (""Id"") ON DELETE CASCADE
                )
            ");
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""CourtScheduleBlocks"" (
                    ""Id""              INTEGER  PRIMARY KEY AUTOINCREMENT,
                    ""CourtId""         INTEGER  NOT NULL,
                    ""DaysOfWeek""      TEXT     NOT NULL DEFAULT '',
                    ""IncludeHolidays"" INTEGER  NOT NULL DEFAULT 0,
                    ""StartHour""       INTEGER  NOT NULL,
                    ""EndHour""         INTEGER  NOT NULL,
                    ""Type""            INTEGER  NOT NULL DEFAULT 0,
                    ""IsActive""        INTEGER  NOT NULL DEFAULT 1,
                    ""Description""     TEXT     NULL,
                    FOREIGN KEY (""CourtId"") REFERENCES ""Courts"" (""Id"") ON DELETE CASCADE
                )
            ");
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"CourtScheduleBlocks\" ADD COLUMN \"IsActive\" INTEGER NOT NULL DEFAULT 1"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"CourtScheduleBlocks\" ADD COLUMN \"Description\" TEXT NULL"); } catch { }
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""FacilityHolidays"" (
                    ""Id""      INTEGER  PRIMARY KEY AUTOINCREMENT,
                    ""OwnerId"" TEXT     NOT NULL,
                    ""Date""    TEXT     NOT NULL,
                    ""Label""   TEXT     NULL
                )
            ");
        }
    }
    catch { /* table already exists or db not ready — non-fatal */ }

    // ── Ensure CourtBundles / CourtBundleCourts / CourtBundleRateBlocks tables exist,
    //    and Bookings gets CourtBundleId/BundleGroupId ─────────────────────────────
    // Bundled multi-court "peak hours" booking — created via raw SQL so no migration
    // file is required (project has no committed migrations).
    try
    {
        if (isPostgres)
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""CourtBundles"" (
                    ""Id""       serial                  PRIMARY KEY,
                    ""OwnerId""  character varying(450)  NOT NULL,
                    ""Name""     character varying(100)  NOT NULL DEFAULT '',
                    ""IsActive"" boolean                 NOT NULL DEFAULT TRUE
                )
            ");
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""CourtBundleCourts"" (
                    ""Id""            serial   PRIMARY KEY,
                    ""CourtBundleId"" integer  NOT NULL,
                    ""CourtId""       integer  NOT NULL,
                    CONSTRAINT ""FK_CourtBundleCourts_CourtBundles_CourtBundleId""
                        FOREIGN KEY (""CourtBundleId"") REFERENCES ""CourtBundles"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_CourtBundleCourts_Courts_CourtId""
                        FOREIGN KEY (""CourtId"") REFERENCES ""Courts"" (""Id"") ON DELETE CASCADE
                )
            ");
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""CourtBundleRateBlocks"" (
                    ""Id""              serial                  PRIMARY KEY,
                    ""CourtBundleId""   integer                 NOT NULL,
                    ""DaysOfWeek""      character varying(40)   NOT NULL DEFAULT '',
                    ""IncludeHolidays"" boolean                 NOT NULL DEFAULT FALSE,
                    ""StartHour""       integer                 NOT NULL,
                    ""EndHour""         integer                 NOT NULL,
                    ""FlatPrice""       numeric(10,2)           NOT NULL DEFAULT 0,
                    ""IsActive""        boolean                 NOT NULL DEFAULT TRUE,
                    CONSTRAINT ""FK_CourtBundleRateBlocks_CourtBundles_CourtBundleId""
                        FOREIGN KEY (""CourtBundleId"") REFERENCES ""CourtBundles"" (""Id"") ON DELETE CASCADE
                )
            ");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Bookings\" ADD COLUMN IF NOT EXISTS \"CourtBundleId\" integer NULL");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Bookings\" ADD COLUMN IF NOT EXISTS \"BundleGroupId\" uuid NULL");
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""CourtBundles"" (
                    ""Id""       INTEGER  PRIMARY KEY AUTOINCREMENT,
                    ""OwnerId""  TEXT     NOT NULL,
                    ""Name""     TEXT     NOT NULL DEFAULT '',
                    ""IsActive"" INTEGER  NOT NULL DEFAULT 1
                )
            ");
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""CourtBundleCourts"" (
                    ""Id""            INTEGER  PRIMARY KEY AUTOINCREMENT,
                    ""CourtBundleId"" INTEGER  NOT NULL,
                    ""CourtId""       INTEGER  NOT NULL,
                    FOREIGN KEY (""CourtBundleId"") REFERENCES ""CourtBundles"" (""Id"") ON DELETE CASCADE,
                    FOREIGN KEY (""CourtId"") REFERENCES ""Courts"" (""Id"") ON DELETE CASCADE
                )
            ");
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""CourtBundleRateBlocks"" (
                    ""Id""              INTEGER  PRIMARY KEY AUTOINCREMENT,
                    ""CourtBundleId""   INTEGER  NOT NULL,
                    ""DaysOfWeek""      TEXT     NOT NULL DEFAULT '',
                    ""IncludeHolidays"" INTEGER  NOT NULL DEFAULT 0,
                    ""StartHour""       INTEGER  NOT NULL,
                    ""EndHour""         INTEGER  NOT NULL,
                    ""FlatPrice""       TEXT     NOT NULL DEFAULT '0',
                    ""IsActive""        INTEGER  NOT NULL DEFAULT 1,
                    FOREIGN KEY (""CourtBundleId"") REFERENCES ""CourtBundles"" (""Id"") ON DELETE CASCADE
                )
            ");
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Bookings\" ADD COLUMN \"CourtBundleId\" INTEGER NULL"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Bookings\" ADD COLUMN \"BundleGroupId\" TEXT NULL"); } catch { }
        }
    }
    catch { /* table already exists or db not ready — non-fatal */ }

    // ── Public sign-up for Admin-Hosted Open Play ────────────────────────────────
    // Adds AllowPublicSignup/MaxPlayers/PricePerHead to CourtScheduleBlocks and a
    // new OpenPlaySignups table — created via raw SQL so no migration file is
    // required (project has no committed migrations).
    try
    {
        if (isPostgres)
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"CourtScheduleBlocks\" ADD COLUMN IF NOT EXISTS \"AllowPublicSignup\" boolean NOT NULL DEFAULT FALSE");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"CourtScheduleBlocks\" ADD COLUMN IF NOT EXISTS \"MaxPlayers\" integer NULL");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"CourtScheduleBlocks\" ADD COLUMN IF NOT EXISTS \"PricePerHead\" numeric(10,2) NULL");
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""OpenPlaySignups"" (
                    ""Id""                      serial                   PRIMARY KEY,
                    ""CourtId""                 integer                  NOT NULL,
                    ""FacilityName""            character varying(100)   NULL,
                    ""UserId""                  character varying(450)   NOT NULL,
                    ""BookingDate""             date                     NOT NULL,
                    ""StartHour""               integer                  NOT NULL,
                    ""EndHour""                 integer                  NOT NULL,
                    ""SpotCount""               integer                  NOT NULL DEFAULT 1,
                    ""PricePerHeadSnapshot""    numeric(10,2)            NOT NULL DEFAULT 0,
                    ""TotalPrice""              numeric(10,2)            NOT NULL DEFAULT 0,
                    ""Status""                  integer                  NOT NULL DEFAULT 0,
                    ""PaymentStatus""           integer                  NOT NULL DEFAULT 0,
                    ""Notes""                   character varying(500)   NULL,
                    ""PaymentMethod""           text                     NULL,
                    ""PaymentReference""        text                     NULL,
                    ""PaymentProofPath""        text                     NULL,
                    ""PaymentProofSubmittedAt"" timestamp with time zone NULL,
                    ""PaidAt""                  timestamp with time zone NULL,
                    ""CreatedAt""               timestamp with time zone NOT NULL DEFAULT NOW(),
                    ""CommissionAmount""        numeric(18,2)            NULL,
                    CONSTRAINT ""FK_OpenPlaySignups_Courts_CourtId""
                        FOREIGN KEY (""CourtId"") REFERENCES ""Courts"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_OpenPlaySignups_AspNetUsers_UserId""
                        FOREIGN KEY (""UserId"") REFERENCES ""AspNetUsers"" (""Id"") ON DELETE CASCADE
                )
            ");
        }
        else
        {
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"CourtScheduleBlocks\" ADD COLUMN \"AllowPublicSignup\" INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"CourtScheduleBlocks\" ADD COLUMN \"MaxPlayers\" INTEGER NULL"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"CourtScheduleBlocks\" ADD COLUMN \"PricePerHead\" TEXT NULL"); } catch { }
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""OpenPlaySignups"" (
                    ""Id""                      INTEGER  PRIMARY KEY AUTOINCREMENT,
                    ""CourtId""                 INTEGER  NOT NULL,
                    ""FacilityName""            TEXT     NULL,
                    ""UserId""                  TEXT     NOT NULL,
                    ""BookingDate""             TEXT     NOT NULL,
                    ""StartHour""               INTEGER  NOT NULL,
                    ""EndHour""                 INTEGER  NOT NULL,
                    ""SpotCount""               INTEGER  NOT NULL DEFAULT 1,
                    ""PricePerHeadSnapshot""    TEXT     NOT NULL DEFAULT '0',
                    ""TotalPrice""              TEXT     NOT NULL DEFAULT '0',
                    ""Status""                  INTEGER  NOT NULL DEFAULT 0,
                    ""PaymentStatus""           INTEGER  NOT NULL DEFAULT 0,
                    ""Notes""                   TEXT     NULL,
                    ""PaymentMethod""           TEXT     NULL,
                    ""PaymentReference""        TEXT     NULL,
                    ""PaymentProofPath""        TEXT     NULL,
                    ""PaymentProofSubmittedAt"" TEXT     NULL,
                    ""PaidAt""                  TEXT     NULL,
                    ""CreatedAt""               TEXT     NOT NULL DEFAULT (datetime('now')),
                    ""CommissionAmount""        TEXT     NULL,
                    FOREIGN KEY (""CourtId"") REFERENCES ""Courts"" (""Id"") ON DELETE CASCADE,
                    FOREIGN KEY (""UserId"") REFERENCES ""AspNetUsers"" (""Id"") ON DELETE CASCADE
                )
            ");
        }
    }
    catch { /* table already exists or db not ready — non-fatal */ }

    // ── Guest booking (no account required) ──────────────────────────────────────
    // Adds IsGuest to AspNetUsers and GuestAccessToken to Bookings/OpenPlaySignups —
    // simple nullable/defaulted column additions via the same idempotent raw-SQL
    // pattern used all session (no table rebuild needed).
    try
    {
        if (isPostgres)
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"AspNetUsers\" ADD COLUMN IF NOT EXISTS \"IsGuest\" boolean NOT NULL DEFAULT FALSE");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Bookings\" ADD COLUMN IF NOT EXISTS \"GuestAccessToken\" uuid NULL");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"OpenPlaySignups\" ADD COLUMN IF NOT EXISTS \"GuestAccessToken\" uuid NULL");
        }
        else
        {
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"AspNetUsers\" ADD COLUMN \"IsGuest\" INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Bookings\" ADD COLUMN \"GuestAccessToken\" TEXT NULL"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"OpenPlaySignups\" ADD COLUMN \"GuestAccessToken\" TEXT NULL"); } catch { }
        }
    }
    catch { /* column already exists or db not ready — non-fatal */ }

    // ── Open Play: names of the other players when a sign-up takes multiple spots ──
    try
    {
        if (isPostgres)
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"OpenPlaySignups\" ADD COLUMN IF NOT EXISTS \"PlayerNames\" character varying(500) NULL");
        else
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"OpenPlaySignups\" ADD COLUMN \"PlayerNames\" TEXT NULL"); } catch { }
    }
    catch { /* column already exists or db not ready — non-fatal */ }

    // ── Add-on rentals (e.g. paddles) attachable to a booking ────────────────────
    try
    {
        if (isPostgres)
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""AddOnItems"" (
                    ""Id""       serial                  PRIMARY KEY,
                    ""OwnerId""  character varying(450)  NOT NULL,
                    ""Name""     character varying(100)  NOT NULL DEFAULT '',
                    ""Price""    numeric(10,2)           NOT NULL DEFAULT 0,
                    ""IsActive"" boolean                 NOT NULL DEFAULT TRUE
                )
            ");
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""BookingAddOns"" (
                    ""Id""          serial          PRIMARY KEY,
                    ""BookingId""   integer         NOT NULL,
                    ""AddOnItemId"" integer         NOT NULL,
                    ""Quantity""    integer         NOT NULL DEFAULT 1,
                    ""UnitPrice""   numeric(10,2)   NOT NULL DEFAULT 0,
                    CONSTRAINT ""FK_BookingAddOns_Bookings_BookingId""
                        FOREIGN KEY (""BookingId"") REFERENCES ""Bookings"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_BookingAddOns_AddOnItems_AddOnItemId""
                        FOREIGN KEY (""AddOnItemId"") REFERENCES ""AddOnItems"" (""Id"") ON DELETE CASCADE
                )
            ");
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""AddOnItems"" (
                    ""Id""       INTEGER  PRIMARY KEY AUTOINCREMENT,
                    ""OwnerId""  TEXT     NOT NULL,
                    ""Name""     TEXT     NOT NULL DEFAULT '',
                    ""Price""    TEXT     NOT NULL DEFAULT '0',
                    ""IsActive"" INTEGER  NOT NULL DEFAULT 1
                )
            ");
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""BookingAddOns"" (
                    ""Id""          INTEGER  PRIMARY KEY AUTOINCREMENT,
                    ""BookingId""   INTEGER  NOT NULL,
                    ""AddOnItemId"" INTEGER  NOT NULL,
                    ""Quantity""    INTEGER  NOT NULL DEFAULT 1,
                    ""UnitPrice""   TEXT     NOT NULL DEFAULT '0',
                    FOREIGN KEY (""BookingId"") REFERENCES ""Bookings"" (""Id"") ON DELETE CASCADE,
                    FOREIGN KEY (""AddOnItemId"") REFERENCES ""AddOnItems"" (""Id"") ON DELETE CASCADE
                )
            ");
        }
    }
    catch { /* table already exists or db not ready — non-fatal */ }

    // ── Staff accounts: limited-access logins scoped to the admin who created them ──
    try
    {
        if (isPostgres)
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"AspNetUsers\" ADD COLUMN IF NOT EXISTS \"EmployerOwnerId\" character varying(450) NULL");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Bookings\" ADD COLUMN IF NOT EXISTS \"LoggedByStaffId\" character varying(450) NULL");
        }
        else
        {
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"AspNetUsers\" ADD COLUMN \"EmployerOwnerId\" TEXT NULL"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Bookings\" ADD COLUMN \"LoggedByStaffId\" TEXT NULL"); } catch { }
        }
    }
    catch { /* column already exists or db not ready — non-fatal */ }

    // ── Staff-logged Open Play walk-in sign-ups ──────────────────────────────────
    try
    {
        if (isPostgres)
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"OpenPlaySignups\" ADD COLUMN IF NOT EXISTS \"LoggedByStaffId\" character varying(450) NULL");
        else
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"OpenPlaySignups\" ADD COLUMN \"LoggedByStaffId\" TEXT NULL"); } catch { }
    }
    catch { /* column already exists or db not ready — non-fatal */ }

    // ── Snapshot the customer's typed name at booking time (guest/walk-in reuse fix) ──
    try
    {
        if (isPostgres)
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Bookings\" ADD COLUMN IF NOT EXISTS \"CustomerNameSnapshot\" character varying(200) NULL");
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"OpenPlaySignups\" ADD COLUMN IF NOT EXISTS \"CustomerNameSnapshot\" character varying(200) NULL");
        }
        else
        {
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Bookings\" ADD COLUMN \"CustomerNameSnapshot\" TEXT NULL"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"OpenPlaySignups\" ADD COLUMN \"CustomerNameSnapshot\" TEXT NULL"); } catch { }
        }
    }
    catch { /* column already exists or db not ready — non-fatal */ }

    foreach (var role in new[] { "Admin", "Customer", "Staff" })
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
