using CourtBooking.Data;
using CourtBooking.Models;
using CourtBooking.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBooking.Controllers;

/// <summary>
/// Developer-only tools: activation key generator + subscription approval.
/// Not linked from any nav; access via /Dev/KeyGen or /Dev/Subscriptions.
/// Protected by a plain password stored in appsettings.json → Dev:Password.
/// </summary>
public class DevController : Controller
{
    private readonly KeyGeneratorService _keyGen;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly string _devPassword;

    public DevController(
        KeyGeneratorService keyGen,
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IConfiguration config)
    {
        _keyGen      = keyGen;
        _db          = db;
        _userManager = userManager;
        _devPassword = config["Dev:Password"]
            ?? throw new InvalidOperationException("Dev:Password is not configured.");
    }

    // GET /Dev/KeyGen
    public IActionResult KeyGen() => View();

    // POST /Dev/KeyGen
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult KeyGen(string password, string email, string plan)
    {
        // Always re-render the form so the user can generate multiple keys
        ViewBag.Attempted = true;

        if (string.IsNullOrWhiteSpace(password) ||
            !string.Equals(password.Trim(), _devPassword, StringComparison.Ordinal))
        {
            ViewBag.Error = "Incorrect developer password.";
            ViewBag.Email = email;
            ViewBag.Plan  = plan;
            return View();
        }

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(plan))
        {
            ViewBag.Error = "Email and plan are required.";
            ViewBag.Email = email;
            ViewBag.Plan  = plan;
            return View();
        }

        var key = _keyGen.GenerateKey(email.Trim(), plan.Trim());

        ViewBag.GeneratedKey = key;
        ViewBag.Email        = email.Trim();
        ViewBag.Plan         = plan.Trim();
        return View();
    }

    // GET /Dev/Subscriptions
    public IActionResult Subscriptions() => View();

    // POST /Dev/Subscriptions  — password check, then show pending submissions
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Subscriptions(string password)
    {
        if (!IsValidPassword(password))
        {
            ViewBag.Error = "Incorrect developer password.";
            return View();
        }

        var settings  = await _db.FacilitySettings.FirstOrDefaultAsync();
        var adminUser = (await _userManager.GetUsersInRoleAsync("Admin")).FirstOrDefault();

        ViewBag.Password  = password;   // pass through to approve/reject forms
        ViewBag.Settings  = settings;
        ViewBag.AdminEmail = adminUser?.Email ?? "(no admin registered yet)";
        return View();
    }

    // POST /Dev/ApproveSubscription
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveSubscription(string password, int id)
    {
        if (!IsValidPassword(password))
            return Unauthorized("Invalid developer password.");

        var settings = await _db.FacilitySettings.FindAsync(id);
        if (settings is null) return NotFound();

        var now  = DateTime.UtcNow;
        var days = string.Equals(settings.SubscriptionPlan, "annual", StringComparison.OrdinalIgnoreCase) ? 365 : 30;

        // Renewal: extend from whichever is later — current expiry or now.
        var baseDate = (settings.IsSubscribed && settings.EffectiveSubscriptionExpiry.HasValue
                            && settings.EffectiveSubscriptionExpiry.Value > now)
                       ? settings.EffectiveSubscriptionExpiry.Value
                       : now;

        settings.IsSubscribed            = true;
        settings.SubscriptionActivatedAt ??= now;
        settings.SubscriptionExpiresAt   = baseDate.AddDays(days);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Subscription for \"{settings.FacilityName}\" approved and activated.";
        return RedirectToAction(nameof(Subscriptions));
    }

    // POST /Dev/RejectSubscription
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectSubscription(string password, int id)
    {
        if (!IsValidPassword(password))
            return Unauthorized("Invalid developer password.");

        var settings = await _db.FacilitySettings.FindAsync(id);
        if (settings is null) return NotFound();

        settings.SubscriptionPlan            = null;
        settings.SubscriptionPaymentRef      = null;
        settings.SubscriptionProofPath       = null;
        settings.SubscriptionSubmittedAt     = null;
        await _db.SaveChangesAsync();

        TempData["Error"] = $"Subscription submission for \"{settings.FacilityName}\" rejected and cleared.";
        return RedirectToAction(nameof(Subscriptions));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool IsValidPassword(string? password) =>
        !string.IsNullOrWhiteSpace(password) &&
        string.Equals(password.Trim(), _devPassword, StringComparison.Ordinal);
}
