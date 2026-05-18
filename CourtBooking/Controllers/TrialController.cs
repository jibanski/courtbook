using CourtBooking.Data;
using CourtBooking.Models;
using CourtBooking.Services;
using CourtBooking.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace CourtBooking.Controllers;

public class TrialController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly EmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<TrialController> _logger;

    public TrialController(ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        EmailService email,
        IConfiguration config,
        ILogger<TrialController> logger)
    {
        _db            = db;
        _userManager   = userManager;
        _signInManager = signInManager;
        _email         = email;
        _config        = config;
        _logger        = logger;
    }

    // GET /Trial/Start
    public IActionResult Start(string? role)
    {
        var effectiveRole = role == "Admin" ? "Admin" : "Customer";
        return View(new TrialRegistrationViewModel { Role = effectiveRole });
    }

    // POST /Trial/Start
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(TrialRegistrationViewModel model)
    {
        if (model.Role == "Admin" && string.IsNullOrWhiteSpace(model.FacilityName))
            ModelState.AddModelError(nameof(model.FacilityName), "Facility name is required for facility owners.");

        if (!ModelState.IsValid)
            return View(model);

        var user = new ApplicationUser
        {
            UserName                 = model.Email,
            Email                    = model.Email,
            FirstName                = model.FirstName,
            LastName                 = model.LastName,
            EmailConfirmed           = true,
            PrivacyPolicyAcceptedAt  = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors)
                ModelState.AddModelError(string.Empty, err.Description);
            return View(model);
        }

        if (model.Role == "Admin")
        {
            await _userManager.AddToRoleAsync(user, "Admin");

            // Each admin gets their own FacilitySettings record
            var slug = await GenerateUniqueSlugAsync(model.FacilityName!);
            var settings = new FacilitySettings
            {
                OwnerId             = user.Id,
                FacilityName        = model.FacilityName!,
                Slug                = slug,
                TrialStartedAt      = DateTime.UtcNow,
                PaymentInstructions = "Please send the exact amount and include your booking reference in the notes."
            };
            _db.FacilitySettings.Add(settings);
            await _db.SaveChangesAsync();

            await _signInManager.SignInAsync(user, isPersistent: false);
            TempData["Success"] = $"Welcome! Your {FacilitySettings.TrialPeriodDays}-day free trial has started. Set up your courts and go live.";

            await SendRegistrationNotificationAsync(user, role: "Facility Owner", facilityName: model.FacilityName);
            await SendWelcomeEmailAsync(user, role: "Admin", facilityName: model.FacilityName);

            return RedirectToAction("Index", "Admin");
        }
        else
        {
            await _userManager.AddToRoleAsync(user, "Customer");

            // Pin this customer to the facility they came from so every future login lands here
            var facilitySlug = Request.Cookies["facilitySlug"];
            if (!string.IsNullOrEmpty(facilitySlug))
            {
                user.PreferredFacilitySlug = facilitySlug;
                await _userManager.UpdateAsync(user);
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
            TempData["Success"] = $"Welcome, {user.FirstName}! Browse courts and make your first booking.";

            await SendRegistrationNotificationAsync(user, role: "Customer");
            await SendWelcomeEmailAsync(user, role: "Customer", facilitySlug: facilitySlug);

            if (!string.IsNullOrEmpty(facilitySlug))
                return RedirectToAction("Index", "Facility", new { slug = facilitySlug });

            return RedirectToAction("Index", "Courts");
        }
    }

    // GET /Trial/Expired
    public IActionResult Expired() => View();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SendRegistrationNotificationAsync(ApplicationUser user, string role, string? facilityName = null)
    {
        var adminEmail   = _config["Subscription:ContactEmail"] ?? "courtbooksolutions@gmail.com";
        var registeredAt = DateTime.UtcNow.AddHours(8).ToString("MMM d, yyyy h:mm tt") + " PHT";
        var facilityLine = !string.IsNullOrWhiteSpace(facilityName)
            ? $"<tr><td style='color:#6c757d;padding:4px 0;'>Facility</td><td style='font-weight:600;padding:4px 0;'>{facilityName}</td></tr>"
            : "";
        var roleColor = role == "Facility Owner" ? "#0d6efd" : "#198754";
        var roleBadge = $"<span style='background:{roleColor};color:#fff;padding:2px 10px;border-radius:12px;font-size:13px;'>{role}</span>";

        var html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:520px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#0d6efd;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;'>CourtBook</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>New Registration</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>A new user just registered on CourtBook:</p>
      <table style='width:100%;border-collapse:collapse;'>
        <tr><td style='color:#6c757d;padding:4px 0;'>Role</td><td style='padding:4px 0;'>{roleBadge}</td></tr>
        <tr><td style='color:#6c757d;padding:4px 0;'>Name</td><td style='font-weight:600;padding:4px 0;'>{user.FullName}</td></tr>
        <tr><td style='color:#6c757d;padding:4px 0;'>Email</td><td style='padding:4px 0;'><a href='mailto:{user.Email}' style='color:#0d6efd;'>{user.Email}</a></td></tr>
        {facilityLine}
        <tr><td style='color:#6c757d;padding:4px 0;'>Registered</td><td style='padding:4px 0;'>{registeredAt}</td></tr>
      </table>
    </div>
    <div style='background:#f8f9fa;color:#6c757d;font-size:12px;padding:14px 24px;border-top:1px solid #e9ecef;'>
      This is an automated notification from CourtBook.
    </div>
  </div>
</body></html>";

        var plain = $"New CourtBook Registration\n\nRole: {role}\nName: {user.FullName}\nEmail: {user.Email}"
                  + (facilityName != null ? $"\nFacility: {facilityName}" : "")
                  + $"\nRegistered: {registeredAt}";

        try
        {
            await _email.SendAsync(adminEmail, $"[CourtBook] New {role} Registered — {user.FullName}", html, plain);
            _logger.LogInformation("[TrialController] Registration notification sent for {Email} ({Role})", user.Email, role);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TrialController] Failed to send registration notification for {Email}", user.Email);
        }
    }

    private async Task SendWelcomeEmailAsync(ApplicationUser user, string role, string? facilityName = null, string? facilitySlug = null)
    {
        var baseUrl      = _config["App:BaseUrl"]?.TrimEnd('/') ?? "https://courtbook-solutions.up.railway.app";
        var contactEmail = _config["Subscription:ContactEmail"] ?? "courtbooksolutions@gmail.com";

        string subject, html, plain;

        if (role == "Admin")
        {
            var dashboardUrl = $"{baseUrl}/Admin";
            subject = $"Welcome to CourtBook, {user.FirstName}! Your 30-day free trial has started.";
            html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:560px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#0d6efd;color:#fff;padding:24px;'>
      <div style='font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;'>CourtBook</div>
      <div style='font-size:24px;font-weight:700;margin-top:6px;'>Welcome, {user.FirstName}! 🎉</div>
    </div>
    <div style='padding:24px;line-height:1.65;font-size:15px;'>
      <p style='margin:0 0 14px;'>Thanks for joining CourtBook! Your <strong>30-day free trial</strong> is now active for <strong>{facilityName}</strong>.</p>
      <p style='margin:0 0 20px;'>Here's how to get started:</p>
      <table style='width:100%;border-collapse:collapse;margin-bottom:20px;'>
        <tr>
          <td style='padding:10px 12px;background:#f8f9fa;border-radius:6px;margin-bottom:8px;display:block;'>
            <strong style='color:#0d6efd;'>Step 1</strong> &nbsp;Add your courts — set names, sport types, pricing, and photos.
          </td>
        </tr>
        <tr><td style='padding:4px;'></td></tr>
        <tr>
          <td style='padding:10px 12px;background:#f8f9fa;border-radius:6px;display:block;'>
            <strong style='color:#0d6efd;'>Step 2</strong> &nbsp;Share your booking link with customers so they can start booking online.
          </td>
        </tr>
        <tr><td style='padding:4px;'></td></tr>
        <tr>
          <td style='padding:10px 12px;background:#f8f9fa;border-radius:6px;display:block;'>
            <strong style='color:#0d6efd;'>Step 3</strong> &nbsp;Confirm bookings and collect GCash or Maya payments — all from your dashboard.
          </td>
        </tr>
      </table>
      <p style='margin:0 0 22px;text-align:center;'>
        <a href='{dashboardUrl}' style='display:inline-block;background:#0d6efd;color:#fff;text-decoration:none;font-weight:600;padding:13px 28px;border-radius:6px;font-size:15px;'>Go to My Dashboard</a>
      </p>
      <p style='margin:0 0 8px;font-size:13px;color:#6c757d;'>Your trial runs for 30 days. After that, keep your facility going for just <strong>₱499/month</strong> or <strong>₱4,788/year</strong>.</p>
      <p style='margin:0;font-size:13px;color:#6c757d;'>Questions? Reply to this email or reach us at <a href='mailto:{contactEmail}' style='color:#0d6efd;'>{contactEmail}</a>.</p>
    </div>
    <div style='background:#f8f9fa;color:#6c757d;font-size:12px;padding:14px 24px;border-top:1px solid #e9ecef;'>
      You're receiving this because you just created a CourtBook facility owner account.
    </div>
  </div>
</body></html>";
            plain = $"Welcome to CourtBook, {user.FirstName}!\n\nYour 30-day free trial for {facilityName} is now active.\n\nGet started:\n1. Add your courts at {dashboardUrl}\n2. Share your booking link with customers\n3. Confirm bookings and collect payments\n\nAfter your trial: ₱499/month or ₱4,788/year.\n\nQuestions? {contactEmail}";
        }
        else
        {
            var courtsUrl = !string.IsNullOrEmpty(facilitySlug)
                            ? $"{baseUrl}/sportshub/{facilitySlug}"
                            : $"{baseUrl}/Courts";
            subject = $"Welcome to CourtBook, {user.FirstName}!";
            html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:560px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#198754;color:#fff;padding:24px;'>
      <div style='font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;'>CourtBook</div>
      <div style='font-size:24px;font-weight:700;margin-top:6px;'>Welcome, {user.FirstName}! 🏆</div>
    </div>
    <div style='padding:24px;line-height:1.65;font-size:15px;'>
      <p style='margin:0 0 14px;'>Your CourtBook account is ready. You can now browse and book sports courts online — no more chasing availability over text or Facebook.</p>
      <p style='margin:0 0 20px;'>Here's what you can do:</p>
      <ul style='padding-left:20px;margin:0 0 20px;color:#495057;'>
        <li style='margin-bottom:8px;'>Browse available courts near you</li>
        <li style='margin-bottom:8px;'>Pick a date and time slot that works for you</li>
        <li style='margin-bottom:8px;'>Pay securely via GCash or Maya</li>
        <li style='margin-bottom:8px;'>Get instant booking confirmation</li>
      </ul>
      <p style='margin:0 0 22px;text-align:center;'>
        <a href='{courtsUrl}' style='display:inline-block;background:#198754;color:#fff;text-decoration:none;font-weight:600;padding:13px 28px;border-radius:6px;font-size:15px;'>Browse Courts Now</a>
      </p>
      <p style='margin:0;font-size:13px;color:#6c757d;'>Need help? Email us at <a href='mailto:{contactEmail}' style='color:#0d6efd;'>{contactEmail}</a>.</p>
    </div>
    <div style='background:#f8f9fa;color:#6c757d;font-size:12px;padding:14px 24px;border-top:1px solid #e9ecef;'>
      You're receiving this because you just created a CourtBook player account.
    </div>
  </div>
</body></html>";
            plain = $"Welcome to CourtBook, {user.FirstName}!\n\nYour account is ready. Browse and book courts at {courtsUrl}.\n\nYou can:\n- Browse available courts\n- Pick a time slot\n- Pay via GCash or Maya\n- Get instant confirmation\n\nNeed help? {contactEmail}";
        }

        try
        {
            await _email.SendAsync(user.Email!, subject, html, plain);
            _logger.LogInformation("[TrialController] Welcome email sent to {Email} ({Role})", user.Email, role);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TrialController] Failed to send welcome email to {Email}", user.Email);
        }
    }

    private async Task<string> GenerateUniqueSlugAsync(string name)
    {
        var base_slug = Regex.Replace(
            Regex.Replace(name.ToLowerInvariant().Replace(" ", "-"), @"[^a-z0-9\-]", ""),
            @"-+", "-").Trim('-');

        if (string.IsNullOrEmpty(base_slug)) base_slug = "facility";

        var slug    = base_slug;
        var counter = 2;
        while (await _db.FacilitySettings.AnyAsync(s => s.Slug == slug))
        {
            slug = $"{base_slug}-{counter++}";
        }
        return slug;
    }
}
