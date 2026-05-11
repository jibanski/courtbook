using CourtBooking.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace CourtBooking.Filters;

public class TrialCheckFilter : IAsyncActionFilter
{
    private readonly ApplicationDbContext _db;

    public TrialCheckFilter(ApplicationDbContext db) => _db = db;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var settings = await _db.FacilitySettings.FirstOrDefaultAsync();
        if (settings is not null && settings.IsTrialExpired)
        {
            context.Result = new RedirectToActionResult("Expired", "Trial", null);
            return;
        }
        await next();
    }
}
