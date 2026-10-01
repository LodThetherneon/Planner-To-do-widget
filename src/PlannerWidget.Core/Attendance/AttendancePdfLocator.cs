using System.Text.RegularExpressions;

namespace PlannerWidget.Core.Attendance;

/// <summary>
/// A jelenléti ív havonta új fájl, pl. „…\2026. március\Név_Jelenléti_március.pdf”.
/// Ez az osztály a beállított útvonalból kitalálja az aktuális hónap fájljának várható helyét.
/// </summary>
public static partial class AttendancePdfLocator
{
    public static readonly string[] MonthNames =
    [
        "január", "február", "március", "április", "május", "június",
        "július", "augusztus", "szeptember", "október", "november", "december",
    ];

    /// <summary>Az útvonalból kiolvasott (év, hónap); az év hiányozhat.</summary>
    public static (int? Year, int Month)? DetectPeriod(string path)
    {
        var fileOrFolder = path;
        int? month = null;
        var lastIndex = -1;
        for (var i = 0; i < MonthNames.Length; i++)
        {
            var index = fileOrFolder.LastIndexOf(MonthNames[i], StringComparison.OrdinalIgnoreCase);
            if (index > lastIndex)
            {
                lastIndex = index;
                month = i + 1;
            }
        }

        if (month is null)
        {
            return null;
        }

        var years = YearRegex().Matches(path);
        int? year = years.Count > 0 ? int.Parse(years[^1].Value) : null;
        return (year, month.Value);
    }

    public static bool IsForMonth(string path, DateOnly date)
    {
        var period = DetectPeriod(path);
        return period is not null
               && period.Value.Month == date.Month
               && (period.Value.Year is null || period.Value.Year == date.Year);
    }

    /// <summary>
    /// A hónap nevét (és ha van, az évet) lecseréli a megadott dátuméra.
    /// Null, ha az útvonalban nincs hónapnév.
    /// </summary>
    public static string? SuggestPathFor(string path, DateOnly date)
    {
        var period = DetectPeriod(path);
        if (period is null)
        {
            return null;
        }

        var oldMonth = MonthNames[period.Value.Month - 1];
        var newMonth = MonthNames[date.Month - 1];
        var result = Regex.Replace(path, Regex.Escape(oldMonth), m => MatchCase(m.Value, newMonth), RegexOptions.IgnoreCase);

        if (period.Value.Year is { } oldYear && oldYear != date.Year)
        {
            result = Regex.Replace(result, $@"(?<!\d){oldYear}(?!\d)", date.Year.ToString());
        }

        return result;
    }

    private static string MatchCase(string original, string replacement)
    {
        if (original.All(c => !char.IsLetter(c) || char.IsUpper(c)))
        {
            return replacement.ToUpperInvariant();
        }

        return char.IsUpper(original[0])
            ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
            : replacement;
    }

    [GeneratedRegex(@"(?<!\d)20\d{2}(?!\d)")]
    private static partial Regex YearRegex();
}
