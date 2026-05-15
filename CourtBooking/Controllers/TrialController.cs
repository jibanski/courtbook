using CourtBooking.Data;
using CourtBooking.Models;
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

    public TrialController(ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        _db            = db;
        _userManager   = userManager;
        _signInManager = signInManager;
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

            if (!string.IsNullOrEmpty(facilitySlug))
                return RedirectToAction("Index", "Facility", new { slug = facilitySlug });

            return RedirectToAction("Index", "Courts");
        }
    }

    // GET /Trial/Expired
    public IActionResult Expired() => View();

    // ── Helpers ───────────────────────────────────────────────────────────────

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
