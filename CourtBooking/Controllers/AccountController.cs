using CourtBooking.Models;
using CourtBooking.Services;
using CourtBooking.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CourtBooking.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly EmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        EmailService email,
        IConfiguration config,
        ILogger<AccountController> logger)
    {
        _userManager   = userManager;
        _signInManager = signInManager;
        _email         = email;
        _config        = config;
        _logger        = logger;
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

            var facilitySlug = Request.Cookies["facilitySlug"];
            if (!string.IsNullOrEmpty(facilitySlug))
            {
                user.PreferredFacilitySlug = facilitySlug;
                await _userManager.UpdateAsync(user);
            }

            await _signInManager.SignInAsync(user, isPersistent: false);

            if (!string.IsNullOrEmpty(facilitySlug))
                return RedirectToAction("Index", "Facility", new { slug = facilitySlug });

            return RedirectToAction("Index", "Courts");
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

        if (result.IsLockedOut)
        {
            ModelState.AddModelError("", "This account has been disabled. Please contact support at sales@courtbook.com.");
            return View(vm);
        }

        if (result.Succeeded)
        {
            var user = await _userManager.FindByEmailAsync(vm.Email);

            // Admins always go to their dashboard — ignore returnUrl / facility cookie
            if (user != null && await _userManager.IsInRoleAsync(user, "Admin"))
                return RedirectToAction("Index", "Admin");

            // Persist the facility the customer arrived from (once — never overwrite an existing preference)
            var cookieSlug = Request.Cookies["facilitySlug"];
            if (user != null && !string.IsNullOrEmpty(cookieSlug)
                && string.IsNullOrEmpty(user.PreferredFacilitySlug))
            {
                user.PreferredFacilitySlug = cookieSlug;
                await _userManager.UpdateAsync(user);
            }

            // Determine redirect: booking deep-link → facility home → generic courts
            var preferredSlug = user?.PreferredFacilitySlug;

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                && !returnUrl.StartsWith("/Account") && !returnUrl.StartsWith("/Trial"))
                return LocalRedirect(returnUrl);

            if (!string.IsNullOrEmpty(preferredSlug))
                return RedirectToAction("Index", "Facility", new { slug = preferredSlug });

            return RedirectToAction("Index", "Courts");
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

    // ──────────────────────────────────────────────────────────────────────────
    // Forgot / Reset Password — uses Identity's built-in token system.
    // To prevent account enumeration we always show the same confirmation
    // regardless of whether the email exists.
    // ──────────────────────────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult ForgotPassword() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _userManager.FindByEmailAsync(vm.Email.Trim());
        if (user is null)
        {
            // Don't reveal that the email isn't registered.
            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }

        // Don't issue tokens for locked-out accounts — those have been disabled
        // by platform support and should go through support instead.
        var isLockedOut = user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow;
        if (isLockedOut)
        {
            _logger.LogWarning("[ForgotPassword] token request blocked for locked-out account {Email}", user.Email);
            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }

        var rawToken = await _userManager.GeneratePasswordResetTokenAsync(user);

        var baseUrl  = _config["App:BaseUrl"]?.TrimEnd('/')
                       ?? $"{Request.Scheme}://{Request.Host}";
        var resetUrl = $"{baseUrl}/Account/ResetPassword" +
                       $"?email={Uri.EscapeDataString(user.Email!)}" +
                       $"&token={Uri.EscapeDataString(rawToken)}";

        var firstName    = string.IsNullOrWhiteSpace(user.FirstName) ? "there" : user.FirstName;
        var contactEmail = _config["Subscription:ContactEmail"] ?? "sales@courtbook.com";

        var html = $@"<!doctype html>
<html><body style=""font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;"">
  <div style=""max-width:560px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;"">
    <div style=""background:#0d6efd;color:#fff;padding:18px 24px;"">
      <div style=""font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;"">CourtBook</div>
      <div style=""font-size:22px;font-weight:700;margin-top:4px;"">Reset your password</div>
    </div>
    <div style=""padding:24px;line-height:1.55;font-size:15px;"">
      <p style=""margin:0 0 14px;"">Hi {firstName},</p>
      <p style=""margin:0 0 18px;"">We received a request to reset the password on your CourtBook account. Click the button below to choose a new one — this link is valid for <strong>1 hour</strong>.</p>
      <p style=""margin:0 0 22px;text-align:center;"">
        <a href=""{resetUrl}"" style=""display:inline-block;background:#0d6efd;color:#fff;text-decoration:none;font-weight:600;padding:12px 22px;border-radius:6px;"">Reset Password</a>
      </p>
      <p style=""margin:0 0 12px;color:#6c757d;font-size:13px;"">If the button doesn't work, copy and paste this URL into your browser:</p>
      <p style=""margin:0 0 18px;word-break:break-all;font-size:12px;color:#6c757d;"">{resetUrl}</p>
      <p style=""margin:0 0 6px;color:#6c757d;font-size:13px;""><strong>Didn't request this?</strong> You can safely ignore this email — your password won't change unless you click the link above. If you're worried about your account's security, reach our team at <a href=""mailto:{contactEmail}"" style=""color:#0d6efd;"">{contactEmail}</a>.</p>
    </div>
    <div style=""background:#f8f9fa;color:#6c757d;font-size:12px;padding:14px 24px;border-top:1px solid #e9ecef;"">
      This email was sent because someone requested a password reset for the CourtBook account associated with this address.
    </div>
  </div>
</body></html>";

        var plain = $@"Reset your CourtBook password

Hi {firstName},

We received a request to reset your password. Open this link within 1 hour to choose a new one:

{resetUrl}

Didn't request this? You can safely ignore this email — your password won't change unless you use the link above.

Need help? {contactEmail}
";

        try
        {
            await _email.SendAsync(user.Email!, "Reset your CourtBook password", html, plain);
        }
        catch (Exception ex)
        {
            // We still show the generic confirmation to the user — don't leak
            // mail-server problems to the form, but make sure ops sees them.
            _logger.LogError(ex, "[ForgotPassword] failed to send reset email to {Email}", user.Email);
        }

        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [HttpGet]
    public IActionResult ForgotPasswordConfirmation() => View();

    [HttpGet]
    public IActionResult ResetPassword(string? email, string? token)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            return RedirectToAction(nameof(Login));

        return View(new ResetPasswordViewModel { Email = email, Token = token });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _userManager.FindByEmailAsync(vm.Email.Trim());
        if (user is null)
        {
            // Don't reveal which step failed.
            return RedirectToAction(nameof(ResetPasswordConfirmation));
        }

        var result = await _userManager.ResetPasswordAsync(user, vm.Token, vm.Password);
        if (result.Succeeded)
            return RedirectToAction(nameof(ResetPasswordConfirmation));

        // Surface the most useful error. Identity returns "Invalid token" when
        // the link is expired, malformed, or already used.
        var msg = result.Errors.FirstOrDefault()?.Description
                  ?? "Could not reset your password. Please request a new link.";
        ModelState.AddModelError("", msg);
        return View(vm);
    }

    [HttpGet]
    public IActionResult ResetPasswordConfirmation() => View();
}
