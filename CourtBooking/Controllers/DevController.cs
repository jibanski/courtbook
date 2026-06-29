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
    private readonly EmailService _email;
    private readonly string _devPassword;

    public DevController(
        KeyGeneratorService keyGen,
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        EmailService email,
        IConfiguration config)
    {
        _keyGen      = keyGen;
        _db          = db;
        _userManager = userManager;
        _email       = email;
        // Empty string means /Dev routes are locked out (password gate always rejects).
        // Set Dev:Password via appsettings.Development.local.json locally,
        // or the Dev__Password environment variable on Railway.
        _devPassword = config["Dev:Password"] ?? string.Empty;
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

        // Load every facility that has a submitted payment. Filter to pending in
        // memory because IsSubscriptionPending is a computed property.
        // Each row pairs with its owner's email so the operator can generate the
        // right key.
        var all = await _db.FacilitySettings
            .Where(s => s.SubscriptionSubmittedAt != null)
            .OrderByDescending(s => s.SubscriptionSubmittedAt)
            .ToListAsync();

        var pending = all.Where(s => s.IsSubscriptionPending).ToList();

        var ownerIds = pending.Where(s => s.OwnerId != null).Select(s => s.OwnerId!).ToList();
        var emailByOwnerId = await _db.Users
            .Where(u => ownerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email);

        ViewBag.Password       = password;
        ViewBag.PendingList    = pending;
        ViewBag.EmailByOwnerId = emailByOwnerId;
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

        settings.IsSubscribed                = true;
        settings.SubscriptionActivatedAt     = now;
        settings.SubscriptionExpiresAt       = baseDate.AddDays(days);
        settings.LastExpiryReminderThreshold = null;
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

        // Clear only the submission-specific fields. Leave SubscriptionPlan,
        // IsSubscribed, ActivatedAt, ExpiresAt intact so an existing subscriber
        // whose renewal is rejected stays on their current paid period.
        settings.SubscriptionPaymentRef      = null;
        settings.SubscriptionProofPath       = null;
        settings.SubscriptionSubmittedAt     = null;
        await _db.SaveChangesAsync();

        TempData["Error"] = $"Submission for \"{settings.FacilityName}\" rejected and cleared.";
        return RedirectToAction(nameof(Subscriptions));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Facility management — platform-superadmin tools for moderating tenants.
    // ──────────────────────────────────────────────────────────────────────────

    // GET /Dev/Facilities
    public async Task<IActionResult> Facilities()
    {
        // After a suspend/lock POST we come back here with the dev password
        // stashed in TempData — re-run the listing so the operator stays on
        // the unlocked page instead of being booted back to the password gate.
        var stashedPwd = TempData["DevPassword"] as string;
        if (IsValidPassword(stashedPwd))
            return await Facilities(stashedPwd!);

        return View(new List<FacilityAdminRow>());
    }

    // POST /Dev/Facilities  — password gate, then list all facilities
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Facilities(string password)
    {
        if (!IsValidPassword(password))
        {
            ViewBag.Error    = "Incorrect developer password.";
            ViewBag.Password = "";
            return View(new List<FacilityAdminRow>());
        }

        var facilities = await _db.FacilitySettings
            .OrderBy(s => s.FacilityName)
            .ToListAsync();

        var ownerIds = facilities.Where(f => f.OwnerId != null).Select(f => f.OwnerId!).ToList();
        var owners   = await _db.Users.Where(u => ownerIds.Contains(u.Id)).ToListAsync();

        var rows = facilities.Select(f =>
        {
            var owner = owners.FirstOrDefault(u => u.Id == f.OwnerId);
            return new FacilityAdminRow(
                Facility:   f,
                OwnerEmail: owner?.Email ?? "(no owner)",
                OwnerName:  owner == null ? "" : $"{owner.FirstName} {owner.LastName}".Trim(),
                IsLocked:   owner?.LockoutEnd is { } end && end > DateTimeOffset.UtcNow,
                LockoutEnd: owner?.LockoutEnd);
        }).ToList();

        ViewBag.Password = password;
        return View(rows);
    }

    // POST /Dev/SuspendFacility
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SuspendFacility(string password, int id, string? reason)
    {
        if (!IsValidPassword(password)) return Unauthorized("Invalid developer password.");

        var f = await _db.FacilitySettings.FindAsync(id);
        if (f is null) return NotFound();

        f.IsSuspended     = true;
        f.SuspendedAt     = DateTime.UtcNow;
        f.SuspendedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        await _db.SaveChangesAsync();

        TempData["Success"] = $"\"{f.FacilityName}\" suspended. Public pages are now hidden.";
        return RedirectToActionFacilities(password);
    }

    // POST /Dev/UnsuspendFacility
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UnsuspendFacility(string password, int id)
    {
        if (!IsValidPassword(password)) return Unauthorized("Invalid developer password.");

        var f = await _db.FacilitySettings.FindAsync(id);
        if (f is null) return NotFound();

        f.IsSuspended     = false;
        f.SuspendedAt     = null;
        f.SuspendedReason = null;
        await _db.SaveChangesAsync();

        TempData["Success"] = $"\"{f.FacilityName}\" reinstated. Public pages are visible again.";
        return RedirectToActionFacilities(password);
    }

    // POST /Dev/LockOwner  — disables a facility owner's login
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> LockOwner(string password, string ownerId)
    {
        if (!IsValidPassword(password)) return Unauthorized("Invalid developer password.");

        var user = await _userManager.FindByIdAsync(ownerId);
        if (user is null) return NotFound();

        // Lock for ~100 years. Setting LockoutEnabled true is required because
        // Identity refuses to apply a lockout end to an account that opted out.
        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));

        TempData["Success"] = $"Owner login disabled for {user.Email}.";
        return RedirectToActionFacilities(password);
    }

    // POST /Dev/UnlockOwner
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UnlockOwner(string password, string ownerId)
    {
        if (!IsValidPassword(password)) return Unauthorized("Invalid developer password.");

        var user = await _userManager.FindByIdAsync(ownerId);
        if (user is null) return NotFound();

        await _userManager.SetLockoutEndDateAsync(user, null);

        TempData["Success"] = $"Owner login restored for {user.Email}.";
        return RedirectToActionFacilities(password);
    }

    // POST /Dev/ChangeBillingModel
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeBillingModel(string password, int id, string billingModel)
    {
        if (!IsValidPassword(password)) return Unauthorized("Invalid developer password.");

        var f = await _db.FacilitySettings.FindAsync(id);
        if (f is null) return NotFound();

        f.BillingModel = billingModel == "Commission" ? "Commission" : "Subscription";
        await _db.SaveChangesAsync();

        TempData["Success"] = $"\"{f.FacilityName}\" switched to {f.BillingModel} model.";
        return RedirectToActionFacilities(password);
    }

    // POST /Dev/ClearCommission
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearCommission(string password, int id)
    {
        if (!IsValidPassword(password)) return Unauthorized("Invalid developer password.");

        var f = await _db.FacilitySettings.FindAsync(id);
        if (f is null) return NotFound();

        f.CommissionTotalPaid             += f.CommissionBalanceOwed;
        f.CommissionBalanceOwed            = 0m;
        f.CommissionPaymentRef             = null;
        f.CommissionPaymentProofPath       = null;
        f.CommissionPaymentSubmittedAt     = null;
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Commission balance cleared for \"{f.FacilityName}\".";
        return RedirectToActionFacilities(password);
    }

    // After a suspend/lock POST we need to land back on the unlocked list. We
    // stash the dev password in TempData (single-use, server-side) and then
    // /Dev/Facilities re-runs the listing logic when that key is present.
    private IActionResult RedirectToActionFacilities(string password)
    {
        TempData["DevPassword"] = password;
        return RedirectToAction(nameof(Facilities));
    }

    public record FacilityAdminRow(
        FacilitySettings Facility,
        string OwnerEmail,
        string OwnerName,
        bool IsLocked,
        DateTimeOffset? LockoutEnd);

    // ──────────────────────────────────────────────────────────────────────────
    // Review moderation
    // ──────────────────────────────────────────────────────────────────────────

    // GET /Dev/Reviews
    public async Task<IActionResult> Reviews()
    {
        var stashedPwd = TempData["DevPassword"] as string;
        if (IsValidPassword(stashedPwd))
            return await Reviews(stashedPwd!);

        return View(new List<Review>());
    }

    // POST /Dev/Reviews
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reviews(string password)
    {
        if (!IsValidPassword(password))
        {
            ViewBag.Error    = "Incorrect developer password.";
            ViewBag.Password = "";
            return View(new List<Review>());
        }

        var reviews = await _db.Reviews
            .OrderByDescending(r => !r.IsApproved)      // unapproved first
            .ThenBy(r => r.DisplayOrder)
            .ThenByDescending(r => r.SubmittedAt)
            .ToListAsync();

        ViewBag.Password = password;
        return View(reviews);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveReview(string password, int id)
    {
        if (!IsValidPassword(password)) return Unauthorized();
        var r = await _db.Reviews.FindAsync(id);
        if (r is null) return NotFound();
        r.IsApproved = true;
        r.ApprovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Approved review by {r.OwnerName}.";
        return RedirectToReviews(password);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UnapproveReview(string password, int id)
    {
        if (!IsValidPassword(password)) return Unauthorized();
        var r = await _db.Reviews.FindAsync(id);
        if (r is null) return NotFound();
        r.IsApproved = false;
        r.IsFeatured = false;
        r.ApprovedAt = null;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Unapproved review by {r.OwnerName}.";
        return RedirectToReviews(password);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> FeatureReview(string password, int id)
    {
        if (!IsValidPassword(password)) return Unauthorized();
        var r = await _db.Reviews.FindAsync(id);
        if (r is null) return NotFound();
        // Featuring implies approved.
        r.IsApproved = true;
        r.IsFeatured = true;
        r.ApprovedAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Featured review by {r.OwnerName}.";
        return RedirectToReviews(password);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UnfeatureReview(string password, int id)
    {
        if (!IsValidPassword(password)) return Unauthorized();
        var r = await _db.Reviews.FindAsync(id);
        if (r is null) return NotFound();
        r.IsFeatured = false;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Removed from homepage.";
        return RedirectToReviews(password);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetReviewOrder(string password, int id, int displayOrder)
    {
        if (!IsValidPassword(password)) return Unauthorized();
        var r = await _db.Reviews.FindAsync(id);
        if (r is null) return NotFound();
        r.DisplayOrder = displayOrder;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Display order updated.";
        return RedirectToReviews(password);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteReview(string password, int id)
    {
        if (!IsValidPassword(password)) return Unauthorized();
        var r = await _db.Reviews.FindAsync(id);
        if (r is null) return NotFound();
        _db.Reviews.Remove(r);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Deleted review by {r.OwnerName}.";
        return RedirectToReviews(password);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditReview(string password, int id, string? title, string body, int rating)
    {
        if (!IsValidPassword(password)) return Unauthorized();
        var r = await _db.Reviews.FindAsync(id);
        if (r is null) return NotFound();
        r.Title  = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        r.Body   = body.Trim();
        r.Rating = Math.Clamp(rating, 1, 5);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Review by {r.OwnerName} updated.";
        return RedirectToReviews(password);
    }

    private IActionResult RedirectToReviews(string password)
    {
        TempData["DevPassword"] = password;
        return RedirectToAction(nameof(Reviews));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Email diagnostics
    // ──────────────────────────────────────────────────────────────────────────

    // GET /Dev/TestEmail
    public IActionResult TestEmail() => View();

    // POST /Dev/TestEmail
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TestEmail(string password, string toEmail)
    {
        if (!IsValidPassword(password))
        {
            ViewBag.Error = "Incorrect developer password.";
            return View();
        }

        ViewBag.Password      = password;
        ViewBag.ToEmail       = toEmail;
        ViewBag.IsConfigured  = _email.IsConfigured;

        if (!_email.IsConfigured)
        {
            ViewBag.ConfigError = "EmailService.IsConfigured = false. " +
                "Check that Email:Provider, Email:FromAddress, and Email:ApiKey (for BrevoHttp) " +
                "are set in Railway environment variables (use double-underscore: Email__Provider, etc.).";
            return View();
        }

        try
        {
            await _email.SendAsync(
                toEmail,
                "[CourtBook] Test Email — Diagnostics",
                "<h2>Test Email</h2><p>If you received this, CourtBook email delivery is working correctly.</p>",
                "Test Email\n\nIf you received this, CourtBook email delivery is working correctly.");

            ViewBag.Success = $"Email sent successfully to {toEmail}. Check the inbox (and spam folder).";
        }
        catch (Exception ex)
        {
            ViewBag.SendError = ex.Message;
        }

        return View();
    }

    // ── Donation QR ───────────────────────────────────────────────────────────

    // GET /Dev/DonationQr
    public async Task<IActionResult> DonationQr(string? password, string? error)
    {
        if (!string.IsNullOrEmpty(error)) ViewBag.Error = error;
        ViewBag.Password = password ?? "";
        ViewBag.IsUnlocked = IsValidPassword(password);
        ViewBag.Config = await _db.PlatformConfig.FindAsync(1);
        return View();
    }

    // POST /Dev/DonationQr
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DonationQr(string password, IFormFile? gcashQr, IFormFile? mayaQr,
                                                 IFormFile? metrobankQr, IFormFile? bpiQr)
    {
        if (!IsValidPassword(password))
            return RedirectToAction(nameof(DonationQr), new { error = "Invalid password." });

        var cfg = await _db.PlatformConfig.FindAsync(1);
        if (cfg == null)
        {
            cfg = new CourtBooking.Models.PlatformConfig { Id = 1 };
            _db.PlatformConfig.Add(cfg);
        }

        async Task SaveQr(IFormFile? file, Action<byte[], string> setter)
        {
            if (file is not { Length: > 0 }) return;
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            setter(ms.ToArray(), file.ContentType);
        }

        await SaveQr(gcashQr,     (d, t) => { cfg.GCashQrData     = d; cfg.GCashQrContentType     = t; });
        await SaveQr(mayaQr,      (d, t) => { cfg.MayaQrData      = d; cfg.MayaQrContentType      = t; });
        await SaveQr(metrobankQr, (d, t) => { cfg.MetrobankQrData = d; cfg.MetrobankQrContentType = t; });
        await SaveQr(bpiQr,       (d, t) => { cfg.BpiQrData       = d; cfg.BpiQrContentType       = t; });

        await _db.SaveChangesAsync();
        TempData["Success"] = "Donation QR codes updated.";
        return RedirectToAction(nameof(DonationQr), new { password });
    }

    // ── Platform Logo ─────────────────────────────────────────────────────────

    // GET /Dev/Logo
    public async Task<IActionResult> Logo(string? password, string? error)
    {
        if (!string.IsNullOrEmpty(error)) ViewBag.Error = error;
        ViewBag.Password   = password ?? "";
        ViewBag.IsUnlocked = IsValidPassword(password);
        ViewBag.Config     = await _db.PlatformConfig.FindAsync(1);
        return View();
    }

    // POST /Dev/Logo
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logo(string password, IFormFile? logo, string? action)
    {
        if (!IsValidPassword(password))
            return RedirectToAction(nameof(Logo), new { error = "Invalid password." });

        // Password-only submit (the unlock form) — just bounce back to the
        // GET so the upload form renders. Mirrors the DonationQr unlock flow.
        if (logo is null && string.IsNullOrEmpty(action))
            return RedirectToAction(nameof(Logo), new { password });

        var cfg = await _db.PlatformConfig.FindAsync(1);
        if (cfg == null)
        {
            cfg = new CourtBooking.Models.PlatformConfig { Id = 1 };
            _db.PlatformConfig.Add(cfg);
        }

        if (string.Equals(action, "remove", StringComparison.OrdinalIgnoreCase))
        {
            cfg.LogoData        = null;
            cfg.LogoContentType = null;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Logo removed — landing page reverted to the default icon.";
            return RedirectToAction(nameof(Logo), new { password });
        }

        if (logo is not { Length: > 0 })
            return RedirectToAction(nameof(Logo), new { password, error = "Choose an image file to upload." });

        // Reject anything that isn't a common web image type.
        var allowed = new[] { "image/png", "image/jpeg", "image/webp", "image/svg+xml" };
        if (!allowed.Contains(logo.ContentType))
            return RedirectToAction(nameof(Logo), new { password, error = "Unsupported image type. Use PNG, JPG, WEBP, or SVG." });

        // Cap upload to 1 MB to keep the data URL embedded in the layout tiny.
        if (logo.Length > 1_048_576)
            return RedirectToAction(nameof(Logo), new { password, error = "Logo file is too large (max 1 MB)." });

        using var ms = new MemoryStream();
        await logo.CopyToAsync(ms);
        cfg.LogoData        = ms.ToArray();
        cfg.LogoContentType = logo.ContentType;

        await _db.SaveChangesAsync();
        TempData["Success"] = "Platform logo updated. Refresh the landing page to see it.";
        return RedirectToAction(nameof(Logo), new { password });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Returns false when _devPassword is empty (not configured) so all /Dev
    // routes silently reject — prevents accidental open access in production.
    private bool IsValidPassword(string? password) =>
        !string.IsNullOrEmpty(_devPassword) &&
        !string.IsNullOrWhiteSpace(password) &&
        string.Equals(password.Trim(), _devPassword, StringComparison.Ordinal);
}