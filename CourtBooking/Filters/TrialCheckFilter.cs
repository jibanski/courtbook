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
        // No enforcement — facility owners are never locked out after their trial.
        // The dashboard shows a soft informational banner instead.
        _ = _db; // suppress unused warning
        await next();
    }
}
