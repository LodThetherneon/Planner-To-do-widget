namespace PlannerWidget.Core.Attendance;

/// <summary>
/// Napfüggő PDF-mezőnevek sablonjai, pl. "ÉRKEZÉS IDEJE{day}" vagy "ÓRASZÁM NAPPAL 6002200{day}".
/// A formátum kompatibilis a régi Python-verzióval ({day} és {day:02d}).
///
/// Miért kell pontozás? A jelenléti ívben a nappali óraszám mezője 1–9-ig "60022001".."60022009",
/// 10-től viszont "600220010". Egyetlen nap mezőnevéből nem dönthető el, hogy a "03" egy nullával
/// kitöltött nap ({day:02d}) vagy egy "0" előtag + a nap ({day}). A régi verzió rosszul döntött,
/// ezért 10-e után nem írta be az óraszámot. Itt minden jelölt sablont lefuttatunk 1–31-re,
/// és azt választjuk, amelyik a legtöbb létező mezőnévre illeszkedik.
/// </summary>
public static class FieldTemplate
{
    public const string DayPlaceholder = "{day}";
    public const string PaddedDayPlaceholder = "{day:02d}";

    public static bool HasPlaceholder(string template) =>
        template.Contains(DayPlaceholder, StringComparison.Ordinal) ||
        template.Contains(PaddedDayPlaceholder, StringComparison.Ordinal);

    public static string Resolve(string template, int day) =>
        template
            .Replace(PaddedDayPlaceholder, day.ToString("00"), StringComparison.Ordinal)
            .Replace(DayPlaceholder, day.ToString(), StringComparison.Ordinal);

    /// <summary>Hány napra (1–31) létezik a sablonból képzett mező.</summary>
    public static int Score(string template, IReadOnlySet<string> fieldNames)
    {
        if (!HasPlaceholder(template))
        {
            return fieldNames.Contains(template) ? 1 : 0;
        }

        var score = 0;
        for (var day = 1; day <= 31; day++)
        {
            if (fieldNames.Contains(Resolve(template, day)))
            {
                score++;
            }
        }

        return score;
    }

    /// <summary>Az összes lehetséges sablon, amely <paramref name="day"/> napra visszaadja a mezőnevet.</summary>
    public static IEnumerable<string> Candidates(string fieldName, int day)
    {
        var plain = day.ToString();
        var padded = day.ToString("00");

        foreach (var index in IndexesOf(fieldName, plain))
        {
            yield return string.Concat(fieldName.AsSpan(0, index), DayPlaceholder, fieldName.AsSpan(index + plain.Length));
        }

        if (padded != plain)
        {
            foreach (var index in IndexesOf(fieldName, padded))
            {
                yield return string.Concat(fieldName.AsSpan(0, index), PaddedDayPlaceholder, fieldName.AsSpan(index + padded.Length));
            }
        }
    }

    /// <summary>
    /// A legjobb sablon a mezőnévhez. Döntetlen esetén a mezőnév végéhez közelebbi
    /// helyettesítés nyer (a sorszám tipikusan a név végén van).
    /// </summary>
    public static (string Template, int Score)? Derive(string fieldName, int day, IReadOnlySet<string> fieldNames)
    {
        (string Template, int Score, int Position)? best = null;
        foreach (var candidate in Candidates(fieldName, day))
        {
            var score = Score(candidate, fieldNames);
            var position = candidate.IndexOf('{');
            if (best is null || score > best.Value.Score || (score == best.Value.Score && position > best.Value.Position))
            {
                best = (candidate, score, position);
            }
        }

        return best is null ? null : (best.Value.Template, best.Value.Score);
    }

    /// <summary>
    /// Meglévő (pl. a régi verzióból importált) sablon javítása: ha nem fedi le a hónap napjait,
    /// egy általa megtalált mezőből újraszármaztatjuk.
    /// </summary>
    public static string Repair(string template, IReadOnlySet<string> fieldNames)
    {
        if (!HasPlaceholder(template))
        {
            return template;
        }

        var current = Score(template, fieldNames);
        if (current >= 28)
        {
            return template;
        }

        var bestTemplate = template;
        var bestScore = current;
        for (var day = 1; day <= 31; day++)
        {
            var name = Resolve(template, day);
            if (!fieldNames.Contains(name))
            {
                continue;
            }

            if (Derive(name, day, fieldNames) is { } derived && derived.Score > bestScore)
            {
                bestTemplate = derived.Template;
                bestScore = derived.Score;
            }
        }

        return bestTemplate;
    }

    private static IEnumerable<int> IndexesOf(string text, string value)
    {
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            yield return index;
            index = text.IndexOf(value, index + 1, StringComparison.Ordinal);
        }
    }
}
