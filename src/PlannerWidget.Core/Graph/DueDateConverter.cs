namespace PlannerWidget.Core.Graph;

/// <summary>
/// A Planner a határidőt UTC időpontként tárolja (pl. "2026-03-05T23:00:00Z").
/// A régi Python-verzió egyszerűen levágta a dátumrészt, ami magyar idő szerint
/// egy nappal korábbi dátumot adhatott. Itt mindig helyi időre konvertálunk.
/// </summary>
public static class DueDateConverter
{
    public static DateOnly? FromGraph(DateTimeOffset? value, TimeZoneInfo? zone = null)
    {
        if (value is null)
        {
            return null;
        }

        var local = TimeZoneInfo.ConvertTime(value.Value, zone ?? TimeZoneInfo.Local);
        return DateOnly.FromDateTime(local.DateTime);
    }

    /// <summary>A helyi nap delét küldjük el UTC-ben, így bármely irányú időzóna-eltolás mellett is az a nap marad.</summary>
    public static string? ToGraph(DateOnly? date, TimeZoneInfo? zone = null)
    {
        if (date is null)
        {
            return null;
        }

        zone ??= TimeZoneInfo.Local;
        var localNoon = date.Value.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(localNoon, zone);
        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
    }
}
