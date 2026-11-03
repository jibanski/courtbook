namespace CourtBooking.Services;

/// <summary>
/// Seasonal look presets. The active key is stored in PlatformConfig.ActiveSeason and
/// applied by _Layout as <c>data-season</c> on &lt;html&gt;; the styling lives in wwwroot/css/seasons.css.
/// </summary>
public static class SeasonTheme
{
    public const string CacheKey = "platform-active-season";

    public static readonly IReadOnlyList<(string Key, string Label, string Emoji, string Message)> All = new[]
    {
        ("halloween", "Halloween", "🎃", "Happy Halloween! Have a spooktacular game day — book your court if you dare."),
        ("christmas", "Christmas", "🎄", "Maligayang Pasko! Wishing you a merry Christmas — play, laugh and celebrate on the court."),
        ("newyear", "New Year", "🎆", "Happy New Year! Here's to a year of great games — see you on the court."),
        ("valentines", "Valentine's", "💘", "Happy Hearts Day! Bring your favorite doubles partner and play together."),
        ("summer", "Summer", "☀️", "Summer is here! Beat the heat — grab a court and make the most of the season."),
        ("independence", "Independence Day", "⭐", "Maligayang Araw ng Kalayaan! Celebrate Independence Day with a game."),
    };

    public static bool IsValid(string? key) => All.Any(s => s.Key == key);
}
