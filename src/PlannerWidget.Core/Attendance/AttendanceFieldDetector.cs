using System.Globalization;
using System.Text;

namespace PlannerWidget.Core.Attendance;

/// <summary>A jelenléti ív mezőnév-sablonjai (kompatibilis a régi planner_defaults.json formátummal).</summary>
public sealed record AttendanceTemplates(
    string Arrival,
    string Leave,
    string Hours,
    string Signature,
    string? TotalHours)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> DailyTemplates => [Arrival, Leave, Hours, Signature];
}

/// <summary>Kitölthető jelenléti ív mezőinek automatikus felismerése.</summary>
public static class AttendanceFieldDetector
{
    /// <summary>Ennyi napra kell illeszkednie egy sablonnak, hogy napi sornak tekintsük.</summary>
    private const int MinimumDailyScore = 2;

    public static AttendanceTemplates? Detect(IReadOnlyCollection<string> fieldNames, int day)
    {
        var set = fieldNames.ToHashSet(StringComparer.Ordinal);

        var arrival = BestDaily(fieldNames, set, day, n => n.Contains("erkez"));
        var leave = BestDaily(fieldNames, set, day, n => n.Contains("tavoz"));
        var hours = BestDaily(fieldNames, set, day, n =>
                        n.Contains("oraszam") && !IsNight(n) && !IsTotal(n))
                    ?? BestDaily(fieldNames, set, day, n =>
                        n.Contains("ora") && !IsNight(n) && !IsTotal(n));
        var signature = BestDaily(fieldNames, set, day, n => n.Contains("alair") || n.Contains("sign"));

        if (arrival is null || leave is null || hours is null || signature is null)
        {
            return null;
        }

        return new AttendanceTemplates(arrival, leave, hours, signature, DetectTotal(fieldNames));
    }

    /// <summary>A havi összesített (nappali) óraszám mezője, ha van.</summary>
    public static string? DetectTotal(IEnumerable<string> fieldNames)
    {
        var totals = fieldNames
            .Where(n => IsTotal(Normalize(n)) && !IsNight(Normalize(n)))
            .ToList();

        return totals.FirstOrDefault(n => Normalize(n).Contains("nappal"))
               ?? totals.FirstOrDefault(n => Normalize(n).Contains("ora"))
               ?? totals.FirstOrDefault();
    }

    /// <summary>Kisbetűs, ékezet nélküli alak a kulcsszavas kereséshez.</summary>
    public static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool IsNight(string normalized) => normalized.Contains("ejszak") || normalized.Contains("night");

    private static bool IsTotal(string normalized) =>
        normalized.Contains("ossz") || normalized.Contains("total") || normalized.Contains("sum");

    private static string? BestDaily(
        IEnumerable<string> fieldNames,
        IReadOnlySet<string> set,
        int day,
        Func<string, bool> keywordFilter)
    {
        (string Template, int Score)? best = null;
        foreach (var name in fieldNames)
        {
            if (!keywordFilter(Normalize(name)))
            {
                continue;
            }

            if (FieldTemplate.Derive(name, day, set) is { } derived &&
                (best is null || derived.Score > best.Value.Score))
            {
                best = derived;
            }
        }

        return best is { Score: >= MinimumDailyScore } ? best.Value.Template : null;
    }
}
