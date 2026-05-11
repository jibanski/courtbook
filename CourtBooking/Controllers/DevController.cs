using CourtBooking.Services;
using Microsoft.AspNetCore.Mvc;

namespace CourtBooking.Controllers;

/// <summary>
/// Developer-only tool for generating CourtBook Pro activation keys.
/// Not linked from any nav; access via /Dev/KeyGen.
/// Protected by a plain password stored in appsettings.json → Dev:Password.
/// </summary>
public class DevController : Controller
{
    private readonly KeyGeneratorService _keyGen;
    private readonly string _devPassword;

    public DevController(KeyGeneratorService keyGen, IConfiguration config)
    {
        _keyGen      = keyGen;
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
}
