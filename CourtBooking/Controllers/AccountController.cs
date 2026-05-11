using CourtBooking.Models;
using CourtBooking.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CourtBooking.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    public IActionResult Register() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = new ApplicationUser
        {
            UserName = vm.Email,
            Email = vm.Email,
            FirstName = vm.FirstName,
            LastName = vm.LastName,
            PhoneNumber = vm.PhoneNumber
        };

        var result = await _userManager.CreateAsync(user, vm.Password);
        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, "Customer");
            await _signInManager.SignInAsync(user, isPersistent: false);

            var facilitySlug = Request.Cookies["facilitySlug"];
            if (!string.IsNullOrEmpty(facilitySlug))
                return RedirectToAction("Index", "Facility", new { slug = facilitySlug });

            return RedirectToAction("Index", "Home");
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError("", error.Description);

        return View(vm);
    }

    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel vm, string? returnUrl = null)
    {
        if (!ModelState.IsValid) return View(vm);

        var result = await _signInManager.PasswordSignInAsync(vm.Email, vm.Password, vm.RememberMe, lockoutOnFailure: false);
        if (result.Succeeded)
        {
            // If a specific page was requested (e.g. from a booking link), go there.
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                && !returnUrl.StartsWith("/Account") && !returnUrl.StartsWith("/Trial"))
                return LocalRedirect(returnUrl);

            // Otherwise, if the user arrived via a facility-specific URL, return them there.
            var facilitySlug = Request.Cookies["facilitySlug"];
            if (!string.IsNullOrEmpty(facilitySlug))
                return RedirectToAction("Index", "Facility", new { slug = facilitySlug });

            return LocalRedirect("/");
        }

        ModelState.AddModelError("", "Invalid email or password.");
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }
}
