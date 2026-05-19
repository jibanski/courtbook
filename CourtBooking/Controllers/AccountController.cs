using CourtBooking.Data;
using CourtBooking.Models;
using CourtBooking.Services;
using CourtBooking.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBooking.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly EmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<AccountController> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ApplicationDbContext _db;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        EmailService email,
        IConfiguration config,
        ILogger<AccountController> logger,
        IServiceScopeFactory scopeFactory,
        ApplicationDbContext db)
    {
        _userManager   = userManager;
        _signInManager = signInManager;
        _email         = email;
        _config        = config;
        _logger        = logger;
        _scopeFactory  = scopeFactory;
        _db            = db;
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
            PhoneNumber = vm.PhoneNumber,
            PrivacyPolicyAcceptedAt = DateTime.UtcNow
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

            await SendRegistrationNotificationAsync(user);
            await SendWelcomeEmailAsync(user, facilitySlug);

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
        // If the user arrived at the login page directly (not via a facility link),
        // clear any stale facilitySlug cookie so they are not silently redirected
        // to a facility they visited days ago.
        var referer = Request.Headers["Referer"].ToString();
        bool fromFacility = !string.IsNullOrEmpty(referer) && referer.Contains("/sportshub/");
        if (!fromFacility && Request.Cookies.ContainsKey("facilitySlug"))
            Response.Cookies.Delete("facilitySlug");

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
            ModelState.AddModelError("", "This account has been disabled. Please contact support at courtbooksolutions@gmail.com.");
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

    // ── My Profile ────────────────────────────────────────────────────────────

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction(nameof(Login));

        var bookingCount = await _db.Bookings
            .CountAsync(b => b.UserId == user.Id);

        var vm = new ProfileViewModel
        {
            FirstName    = user.FirstName,
            LastName     = user.LastName,
            PhoneNumber  = user.PhoneNumber,
            Email        = user.Email,
            CreatedAt    = user.CreatedAt,
            BookingCount = bookingCount,
            HasPassword  = await _userManager.HasPasswordAsync(user)
        };
        return View(vm);
    }

    [Authorize]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel vm)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction(nameof(Login));

        // Password fields are optional — only validate when user is actually trying to change it
        if (string.IsNullOrEmpty(vm.NewPassword))
        {
            ModelState.Remove(nameof(vm.CurrentPassword));
            ModelState.Remove(nameof(vm.NewPassword));
            ModelState.Remove(nameof(vm.ConfirmNewPassword));
        }

        // Repopulate read-only fields before returning on error
        vm.Email        = user.Email;
        vm.CreatedAt    = user.CreatedAt;
        vm.HasPassword  = await _userManager.HasPasswordAsync(user);
        vm.BookingCount = await _db.Bookings.CountAsync(b => b.UserId == user.Id);

        if (!ModelState.IsValid)
            return View(vm);

        // Update personal info
        user.FirstName   = vm.FirstName.Trim();
        user.LastName    = vm.LastName.Trim();
        user.PhoneNumber = vm.PhoneNumber?.Trim();

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var e in updateResult.Errors)
                ModelState.AddModelError("", e.Description);
            return View(vm);
        }

        // Change password if the user filled in the new password field
        if (!string.IsNullOrEmpty(vm.NewPassword))
        {
            if (vm.HasPassword && string.IsNullOrEmpty(vm.CurrentPassword))
            {
                ModelState.AddModelError(nameof(vm.CurrentPassword), "Please enter your current password.");
                return View(vm);
            }

            IdentityResult pwResult;
            if (vm.HasPassword)
                pwResult = await _userManager.ChangePasswordAsync(user, vm.CurrentPassword!, vm.NewPassword);
            else
                pwResult = await _userManager.AddPasswordAsync(user, vm.NewPassword);

            if (!pwResult.Succeeded)
            {
                foreach (var e in pwResult.Errors)
                    ModelState.AddModelError("", e.Description);
                return View(vm);
            }

            await _signInManager.RefreshSignInAsync(user);
            TempData["Success"] = "Profile and password updated successfully.";
        }
        else
        {
            TempData["Success"] = "Profile updated successfully.";
        }

        return RedirectToAction(nameof(Profile));
    }

    // ── External (OAuth) Login ─────────────────────────────────────────────

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult ExternalLogin(string provider, string? returnUrl = null)
    {
        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new { returnUrl });
        var properties  = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return Challenge(properties, provider);
    }

    [HttpGet]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null)
    {
        if (remoteError != null)
        {
            TempData["Error"] = $"Social login error: {remoteError}";
            return RedirectToAction(nameof(Login));
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            TempData["Error"] = "Could not retrieve social login info. Please try again.";
            return RedirectToAction(nameof(Login));
        }

        // Sign in with an existing linked login
        var signInResult = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);

        if (signInResult.Succeeded)
        {
            var existingUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            return await RedirectAfterSocialLoginAsync(existingUser, returnUrl);
        }

        // No linked login found — look up by email or create a new account
        var email = info.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        if (string.IsNullOrEmpty(email))
        {
            TempData["Error"] = "Your social account didn't share an email address. Please register with email instead.";
            return RedirectToAction(nameof(Login));
        }

        var userByEmail = await _userManager.FindByEmailAsync(email);
        if (userByEmail != null)
        {
            // Link this social provider to the existing account and sign in
            await _userManager.AddLoginAsync(userByEmail, info);

            // Persist facility cookie if not already set
            var cookieSlugExisting = Request.Cookies["facilitySlug"];
            if (!string.IsNullOrEmpty(cookieSlugExisting) && string.IsNullOrEmpty(userByEmail.PreferredFacilitySlug))
            {
                userByEmail.PreferredFacilitySlug = cookieSlugExisting;
                await _userManager.UpdateAsync(userByEmail);
            }

            await _signInManager.SignInAsync(userByEmail, isPersistent: false);
            return await RedirectAfterSocialLoginAsync(userByEmail, returnUrl);
        }

        // Brand-new user — create a Customer account automatically
        var firstName = info.Principal.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value ?? "";
        var lastName  = info.Principal.FindFirst(System.Security.Claims.ClaimTypes.Surname)?.Value   ?? "";
        if (string.IsNullOrEmpty(firstName) && string.IsNullOrEmpty(lastName))
        {
            var fullName = info.Principal.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? email;
            var parts    = fullName.Trim().Split(' ', 2);
            firstName    = parts[0];
            lastName     = parts.Length > 1 ? parts[1] : "";
        }

        var newUser = new ApplicationUser
        {
            UserName                = email,
            Email                   = email,
            FirstName               = firstName,
            LastName                = lastName,
            EmailConfirmed          = true,
            PrivacyPolicyAcceptedAt = DateTime.UtcNow
        };

        var createResult = await _userManager.CreateAsync(newUser);
        if (!createResult.Succeeded)
        {
            TempData["Error"] = "Could not create account. " + string.Join(" ", createResult.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Login));
        }

        await _userManager.AddToRoleAsync(newUser, "Customer");
        await _userManager.AddLoginAsync(newUser, info);

        var cookieSlug = Request.Cookies["facilitySlug"];
        if (!string.IsNullOrEmpty(cookieSlug))
        {
            newUser.PreferredFacilitySlug = cookieSlug;
            await _userManager.UpdateAsync(newUser);
        }

        await _signInManager.SignInAsync(newUser, isPersistent: false);
        await SendWelcomeEmailAsync(newUser, cookieSlug);

        TempData["Success"] = $"Welcome, {newUser.FirstName}! Your account has been created.";

        if (!string.IsNullOrEmpty(cookieSlug))
            return RedirectToAction("Index", "Facility", new { slug = cookieSlug });

        return RedirectToAction("Index", "Courts");
    }

    private async Task<IActionResult> RedirectAfterSocialLoginAsync(ApplicationUser? user, string? returnUrl)
    {
        if (user == null) return RedirectToAction(nameof(Login));

        if (await _userManager.IsInRoleAsync(user, "Admin"))
            return RedirectToAction("Index", "Admin");

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            && !returnUrl.StartsWith("/Account") && !returnUrl.StartsWith("/Trial"))
            return LocalRedirect(returnUrl);

        if (!string.IsNullOrEmpty(user.PreferredFacilitySlug))
            return RedirectToAction("Index", "Facility", new { slug = user.PreferredFacilitySlug });

        return RedirectToAction("Index", "Courts");
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
        _logger.LogInformation("[ForgotPassword] POST received for {Email} (modelValid={Valid})",
            vm?.Email ?? "(null)", ModelState.IsValid);

        if (!ModelState.IsValid) return View(vm);

        var user = await _userManager.FindByEmailAsync(vm.Email.Trim());
        if (user is null)
        {
            _logger.LogInformation("[ForgotPassword] no account found for {Email} — silent redirect", vm.Email);
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

        _logger.LogInformation("[ForgotPassword] generating reset token for {Email}", user.Email);

        var rawToken = await _userManager.GeneratePasswordResetTokenAsync(user);

        var baseUrl  = _config["App:BaseUrl"]?.TrimEnd('/')
                       ?? $"{Request.Scheme}://{Request.Host}";
        var resetUrl = $"{baseUrl}/Account/ResetPassword" +
                       $"?email={Uri.EscapeDataString(user.Email!)}" +
                       $"&token={Uri.EscapeDataString(rawToken)}";

        var firstName    = string.IsNullOrWhiteSpace(user.FirstName) ? "there" : user.FirstName;
        var contactEmail = _config["Subscription:ContactEmail"] ?? "courtbooksolutions@gmail.com";

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

        // Fire-and-forget the send so the user gets the confirmation page
        // immediately. SMTP timeouts can take up to 30s; we don't want the
        // browser to hang on the redirect. Errors are written to the app
        // log for ops to inspect.
        var toAddress    = user.Email!;
        var scopeFactory = _scopeFactory;

        _logger.LogInformation("[ForgotPassword] dispatching background email send to {Email}", toAddress);

        _ = Task.Run(async () =>
        {
            ILogger<AccountController>? bgLogger = null;
            try
            {
                using var scope = scopeFactory.CreateScope();
                bgLogger     = scope.ServiceProvider.GetRequiredService<ILogger<AccountController>>();
                var bgEmail  = scope.ServiceProvider.GetRequiredService<EmailService>();

                bgLogger.LogInformation("[ForgotPassword][BG] starting send for {Email}", toAddress);
                await bgEmail.SendAsync(toAddress, "Reset your CourtBook password", html, plain);
                bgLogger.LogInformation("[ForgotPassword][BG] send completed for {Email}", toAddress);
            }
            catch (Exception ex)
            {
                // Log to whichever logger we managed to resolve, otherwise to
                // Console as a last resort so the failure isn't completely silent.
                if (bgLogger is not null)
                    bgLogger.LogError(ex, "[ForgotPassword][BG] failed to send reset email to {Email}", toAddress);
                else
                    Console.Error.WriteLine($"[ForgotPassword][BG] failed before logger acquired: {ex}");
            }
        });

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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SendWelcomeEmailAsync(ApplicationUser user, string? facilitySlug = null)
    {
        var baseUrl      = _config["App:BaseUrl"]?.TrimEnd('/') ?? "https://courtbook-solutions.up.railway.app";
        var contactEmail = _config["Subscription:ContactEmail"] ?? "courtbooksolutions@gmail.com";
        var courtsUrl    = !string.IsNullOrEmpty(facilitySlug)
                           ? $"{baseUrl}/sportshub/{facilitySlug}"
                           : $"{baseUrl}/Courts";

        var html = $@"<!doctype html>
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

        var plain = $"Welcome to CourtBook, {user.FirstName}!\n\nYour account is ready. Browse and book courts at {courtsUrl}.\n\nYou can:\n- Browse available courts\n- Pick a time slot\n- Pay via GCash or Maya\n- Get instant confirmation\n\nNeed help? {contactEmail}";

        try
        {
            await _email.SendAsync(user.Email!, $"Welcome to CourtBook, {user.FirstName}!", html, plain);
            _logger.LogInformation("[AccountController] Welcome email sent to {Email}", user.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AccountController] Failed to send welcome email to {Email}", user.Email);
        }
    }

    private async Task SendRegistrationNotificationAsync(ApplicationUser user)
    {
        var notifyEmails = new[] { "jayben_labrada@yahoo.com", "jibanski@gmail.com" };
        var registeredAt = DateTime.UtcNow.AddHours(8).ToString("MMM d, yyyy h:mm tt") + " PHT";

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
        <tr><td style='color:#6c757d;padding:4px 0;'>Role</td><td style='padding:4px 0;'><span style='background:#198754;color:#fff;padding:2px 10px;border-radius:12px;font-size:13px;'>Customer</span></td></tr>
        <tr><td style='color:#6c757d;padding:4px 0;'>Name</td><td style='font-weight:600;padding:4px 0;'>{user.FullName}</td></tr>
        <tr><td style='color:#6c757d;padding:4px 0;'>Email</td><td style='padding:4px 0;'><a href='mailto:{user.Email}' style='color:#0d6efd;'>{user.Email}</a></td></tr>
        <tr><td style='color:#6c757d;padding:4px 0;'>Registered</td><td style='padding:4px 0;'>{registeredAt}</td></tr>
      </table>
    </div>
    <div style='background:#f8f9fa;color:#6c757d;font-size:12px;padding:14px 24px;border-top:1px solid #e9ecef;'>
      This is an automated notification from CourtBook.
    </div>
  </div>
</body></html>";

        var plain = $"New CourtBook Registration\n\nRole: Customer\nName: {user.FullName}\nEmail: {user.Email}\nRegistered: {registeredAt}";

        foreach (var recipient in notifyEmails)
        {
            try
            {
                await _email.SendAsync(recipient, $"[CourtBook] New Customer Registered — {user.FullName}", html, plain);
                _logger.LogInformation("[AccountController] Registration notification sent to {Recipient} for {Email}", recipient, user.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AccountController] Failed to send registration notification to {Recipient} for {Email}", recipient, user.Email);
            }
        }
    }
}
