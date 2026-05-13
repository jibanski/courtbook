using Microsoft.AspNetCore.Mvc;

namespace CourtBooking.Controllers;

/// <summary>
/// Preserves the old /f/{slug} and /f/{slug}/book/{courtId} URLs after the
/// public facility route was renamed to /sportshub/{slug}. Returns a 301
/// (permanent) redirect so search engines and browsers update their caches,
/// and so any links facility owners already shared with their customers keep
/// resolving without us having to chase down every distribution channel.
/// </summary>
[Route("f")]
public class LegacyFacilityRedirectController : Controller
{
    [Route("{slug}")]
    public IActionResult Index(string slug)
    {
        var qs = Request.QueryString.HasValue ? Request.QueryString.Value : string.Empty;
        return RedirectPermanent($"/sportshub/{slug}{qs}");
    }

    [Route("{slug}/book/{courtId:int}")]
    public IActionResult BookCourt(string slug, int courtId)
    {
        var qs = Request.QueryString.HasValue ? Request.QueryString.Value : string.Empty;
        return RedirectPermanent($"/sportshub/{slug}/book/{courtId}{qs}");
    }
}
