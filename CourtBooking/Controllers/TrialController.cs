using CourtBooking.Data;
using CourtBooking.Models;
using CourtBooking.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    public async Task<IActionResult> Start(string? role)
    {
        bool adminExists = (await _userManager.GetUsersInRoleAsync("Admin")).Any();
        ViewBag.AdminExists = adminExists;

        // If the admin slot is already taken, pre-select Customer so the form is usable
        var effectiveRole = (role == "Admin" && !adminExists) ? "Admin" : "Customer";

        return View(new TrialRegistrationViewModel { Role = effectiveRole });
    }

    // POST /Trial/Start
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(TrialRegistrationViewModel model)
    {
        ViewBag.AdminExists = (await _userManager.GetUsersInRoleAsync("Admin")).Any();

        // Facility name is required for the Admin (owner) role
        if (model.Role == "Admin" && string.IsNullOrWhiteSpace(model.FacilityName))
            ModelState.AddModelError(nameof(model.FacilityName), "Facility name is required for facility owners.");

        // Only one admin per instance
        if (model.Role == "Admin" && (bool)ViewBag.AdminExists)
            ModelState.AddModelError(string.Empty,
                "A facility owner account already exists for this installation. Please log in or contact your administrator.");

        if (!ModelState.IsValid)
            return View(model);

        var user = new ApplicationUser
        {
            UserName       = model.Email,
            Email          = model.Email,
            FirstName      = model.FirstName,
            LastName       = model.LastName,
            EmailConfirmed = true
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

            // Record trial start in FacilitySettings
            var settings = await _db.FacilitySettings.FirstOrDefaultAsync();
            if (settings is null)
            {
                settings = new FacilitySettings { Id = 1 };
                _db.FacilitySettings.Add(settings);
            }
            settings.FacilityName   = model.FacilityName!;
            settings.TrialStartedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _signInManager.SignInAsync(user, isPersistent: false);
            TempData["Success"] = "Welcome! Your 7-day free trial has started. Set up your courts and go live.";
            return RedirectToAction("Index", "Admin");
        }
        else
        {
            await _userManager.AddToRoleAsync(user, "Customer");
            await _signInManager.SignInAsync(user, isPersistent: false);
            TempData["Success"] = $"Welcome, {user.FirstName}! Browse courts and make your first booking.";
            return RedirectToAction("Index", "Courts");
        }
    }

    // GET /Trial/Expired
    public IActionResult Expired() => View();
}
