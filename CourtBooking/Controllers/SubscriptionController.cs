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

        // Already subscribed — go back to dashboard
        if (settings?.IsSubscribed == true)
        {
            TempData["Success"] = "Your subscription is already active.";
            return RedirectToAction("Index", "Admin");
        }

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
        if (settings?.IsSubscribed == true)
            return RedirectToAction("Index", "Admin");

        ViewBag.ContactEmail = _config["Subscription:ContactEmail"] ?? "sales@courtbook.com";
        ViewBag.ContactPhone = _config["Subscription:ContactPhone"] ?? "";
        ViewBag.Settings     = settings;
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

        settings.IsSubscribed            = true;
        settings.SubscriptionActivatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = "🎉 Subscription activated! Welcome to CourtBook Pro.";
        return RedirectToAction("Index", "Admin");
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
