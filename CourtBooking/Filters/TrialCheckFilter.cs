using CourtBooking.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CourtBooking.Filters;

public class TrialCheckFilter : IAsyncActionFilter
{
    private readonly ApplicationDbContext _db;

    public TrialCheckFilter(ApplicationDbContext db) => _db = db;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var userId   = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var settings = await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == userId);
        if (settings is not null && settings.IsTrialExpired)
        {
            context.Result = new RedirectToActionResult("Expired", "Trial", null);
            return;
        }
        await next();
    }
}
