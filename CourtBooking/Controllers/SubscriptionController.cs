using CourtBooking.Data;
using CourtBooking.Models;
using CourtBooking.Services;
using CourtBooking.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CourtBooking.Controllers;

[Authorize(Roles = "Admin")]
public class SubscriptionController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly KeyGeneratorService _keyGen;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionController(
        ApplicationDbContext db,
        IConfiguration config,
        KeyGeneratorService keyGen,
        UserManager<ApplicationUser> userManager)
    {
        _db          = db;
        _config      = config;
        _keyGen      = keyGen;
        _userManager = userManager;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private async Task<FacilitySettings?> GetMySettingsAsync() =>
        await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == CurrentUserId);

    // GET /Subscription/Upgrade
    public async Task<IActionResult> Upgrade()
    {
        var settings = await GetMySettingsAsync();

        // Active subscribers may visit this page to RENEW (early or after expiry).
        // We only bounce people who are healthy AND not yet in the 14-day window,
        // so they don't accidentally double-pay months in advance.
        if (settings?.IsSubscribed == true
            && !settings.IsSubscriptionExpired
            && !settings.IsSubscriptionExpiringSoon)
        {
            TempData["Success"] = "Your subscription is active. You can renew once you're within 14 days of expiry.";
            return RedirectToAction("Settings", "Admin");
        }

        // Pass renewal context to the view (used for header copy & button text).
        ViewBag.IsRenewal           = settings?.IsSubscribed == true;
        ViewBag.CurrentExpiry       = settings?.EffectiveSubscriptionExpiry;
        ViewBag.IsExpired           = settings?.IsSubscriptionExpired ?? false;
        ViewBag.DaysRemaining       = settings?.SubscriptionDaysRemaining ?? 0;

        return View(BuildViewModel());
    }

    // POST /Subscription/Upgrade
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Upgrade(SubscriptionUpgradeViewModel model)
    {
        // Re-attach config values (they're not posted)
        var vm = BuildViewModel();
        model.MonthlyPrice = vm.MonthlyPrice;
        model.AnnualPrice  = vm.AnnualPrice;
        model.GCashNumber  = vm.GCashNumber;
        model.GCashName    = vm.GCashName;
        model.MayaNumber   = vm.MayaNumber;
        model.MayaName     = vm.MayaName;

        if (model.Proof is not { Length: > 0 })
            ModelState.AddModelError(nameof(model.Proof), "Please upload your payment screenshot as proof.");

        if (!ModelState.IsValid)
            return View(model);

        // Save proof file
        var proofPath = await SaveProofAsync(model.Proof!);

        // Update FacilitySettings
        var settings = await GetMySettingsAsync();
        if (settings is null)
        {
            settings = new FacilitySettings { OwnerId = CurrentUserId };
            _db.FacilitySettings.Add(settings);
        }

        settings.SubscriptionPlan        = model.Plan;
        settings.SubscriptionPaymentRef  = model.ReferenceNumber.Trim();
        settings.SubscriptionProofPath   = proofPath;
        settings.SubscriptionSubmittedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        TempData["Success"] = "Payment submitted! We'll verify and send you an activation key within 24 hours.";
        return RedirectToAction(nameof(Pending));
    }

    // GET /Subscription/Pending
    public async Task<IActionResult> Pending()
    {
        var settings = await GetMySettingsAsync();

        // Bounce only fully-paid users without a pending submission.
        // Renewers (already subscribed but with a fresh submission waiting to be
        // verified) must be allowed in so they see confirmation.
        if (settings?.IsSubscribed == true && !settings.IsSubscriptionPending)
            return RedirectToAction("Index", "Admin");

        ViewBag.ContactEmail = _config["Subscription:ContactEmail"] ?? "courtbooksolutions@gmail.com";
        ViewBag.ContactPhone = _config["Subscription:ContactPhone"] ?? "";
        ViewBag.Settings     = settings;
        ViewBag.IsRenewal    = settings?.IsRenewalPending ?? false;
        return View();
    }

    // POST /Subscription/Activate  — enter the key received from CourtBook
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(ActivationKeyViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Please enter a valid activation key.";
            return RedirectToAction("Settings", "Admin");
        }

        // Resolve the CURRENT admin's email for HMAC verification.
        // (Previously this grabbed "the first admin in the DB", which broke
        //  activation for every owner except the very first registered one.)
        var currentAdmin = await _userManager.FindByIdAsync(CurrentUserId);
        if (currentAdmin?.Email is null)
        {
            TempData["Error"] = "Could not identify your admin account. Please contact support.";
            return RedirectToAction("Settings", "Admin");
        }

        var settings = await GetMySettingsAsync();
        var plan     = settings?.SubscriptionPlan;   // may be null if skipping payment flow

        if (!_keyGen.VerifyKey(model.Key, currentAdmin.Email, plan))
        {
            TempData["Error"] = "Activation key is incorrect. Please check and try again.";
            return RedirectToAction("Settings", "Admin");
        }

        if (settings is null)
        {
            settings = new FacilitySettings { OwnerId = CurrentUserId };
            _db.FacilitySettings.Add(settings);
        }

        var now  = DateTime.UtcNow;
        var days = string.Equals(plan, "annual", StringComparison.OrdinalIgnoreCase) ? 365 : 30;

        // Renewal: extend from whichever is later — current expiry or now —
        // so an early renewer keeps the days they've already paid for.
        // First-time activation: just start the clock now.
        var baseDate = (settings.IsSubscribed && settings.EffectiveSubscriptionExpiry.HasValue
                            && settings.EffectiveSubscriptionExpiry.Value > now)
                       ? settings.EffectiveSubscriptionExpiry.Value
                       : now;

        var wasRenewal = settings.IsSubscribed;

        settings.IsSubscribed                = true;
        settings.SubscriptionActivatedAt     = now;        // stamp current paid period start (also clears "pending")
        settings.SubscriptionExpiresAt       = baseDate.AddDays(days);
        settings.LastExpiryReminderThreshold = null;       // reset reminders for the new cycle
        await _db.SaveChangesAsync();

        TempData["Success"] = wasRenewal
            ? $"🎉 Subscription renewed! Your new expiry date is {settings.SubscriptionExpiresAt:MMMM d, yyyy}."
            : "🎉 Subscription activated! Welcome to CourtBook Pro.";
        return RedirectToAction("Index", "Admin");
    }

    // ── Commission payment ────────────────────────────────────────────────────

    // GET /Subscription/Commission
    public async Task<IActionResult> Commission()
    {
        var settings = await GetMySettingsAsync();
        if (settings?.IsCommissionModel != true)
            return RedirectToAction("Index", "Admin");

        ViewBag.Settings     = settings;
        ViewBag.GCashNumber  = _config["Subscription:GCashNumber"] ?? "";
        ViewBag.GCashName    = _config["Subscription:GCashName"]   ?? "";
        ViewBag.MayaNumber   = _config["Subscription:MayaNumber"]  ?? "";
        ViewBag.MayaName     = _config["Subscription:MayaName"]    ?? "";
        ViewBag.ContactEmail = _config["Subscription:ContactEmail"] ?? "courtbooksolutions@gmail.com";
        return View();
    }

    // POST /Subscription/Commission
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Commission(string referenceNumber, IFormFile? proof)
    {
        var settings = await GetMySettingsAsync();
        if (settings?.IsCommissionModel != true)
            return RedirectToAction("Index", "Admin");

        if (string.IsNullOrWhiteSpace(referenceNumber))
            ModelState.AddModelError(nameof(referenceNumber), "Reference number is required.");
        if (proof is not { Length: > 0 })
            ModelState.AddModelError(nameof(proof), "Please upload your payment screenshot.");

        if (!ModelState.IsValid)
        {
            ViewBag.Settings     = settings;
            ViewBag.GCashNumber  = _config["Subscription:GCashNumber"] ?? "";
            ViewBag.GCashName    = _config["Subscription:GCashName"]   ?? "";
            ViewBag.MayaNumber   = _config["Subscription:MayaNumber"]  ?? "";
            ViewBag.MayaName     = _config["Subscription:MayaName"]    ?? "";
            ViewBag.ContactEmail = _config["Subscription:ContactEmail"] ?? "courtbooksolutions@gmail.com";
            return View();
        }

        var proofPath = await SaveProofAsync(proof!);
        settings.CommissionPaymentRef             = referenceNumber.Trim();
        settings.CommissionPaymentProofPath       = proofPath;
        settings.CommissionPaymentSubmittedAt     = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Commission payment submitted! We'll verify and clear your balance within 24 hours.";
        return RedirectToAction("Settings", "Admin");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private SubscriptionUpgradeViewModel BuildViewModel() => new()
    {
        MonthlyPrice = int.TryParse(_config["Subscription:MonthlyPrice"], out var mp) ? mp : 999,
        AnnualPrice  = int.TryParse(_config["Subscription:AnnualPrice"],  out var ap) ? ap : 8999,
        GCashNumber  = _config["Subscription:GCashNumber"] ?? "",
        GCashName    = _config["Subscription:GCashName"]   ?? "",
        MayaNumber   = _config["Subscription:MayaNumber"]  ?? "",
        MayaName     = _config["Subscription:MayaName"]    ?? "",
    };

    private async Task<string> SaveProofAsync(IFormFile file)
    {
        var ext      = Path.GetExtension(file.FileName).ToLowerInvariant();
        var root     = Environment.GetEnvironmentVariable("UPLOADS_ROOT")
                       ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var dir      = Path.Combine(root, "uploads", "subscription");
        Directory.CreateDirectory(dir);
        var fileName = $"sub_{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(dir, fileName);
        await using var stream = System.IO.File.Create(fullPath);
        await file.CopyToAsync(stream);
        return $"/uploads/subscription/{fileName}";
    }
}
