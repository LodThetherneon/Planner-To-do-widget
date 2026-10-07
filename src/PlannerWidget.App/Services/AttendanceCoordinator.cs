using PlannerWidget.Core.Attendance;
using PlannerWidget.Core.Logging;
using PlannerWidget.Core.Settings;

namespace PlannerWidget.App.Services;

public sealed record AttendanceTarget(string PdfPath, AttendanceTemplates Templates);

/// <summary>
/// A jelenléti ív előkészítésének párbeszédes része: PDF kiválasztása, hónapváltás felismerése,
/// mezők automatikus felismerése vagy kézi beállítása. A tényleges PDF-írás az <see cref="AttendanceService"/>-ben van.
/// </summary>
public sealed class AttendanceCoordinator(SettingsStore settings, AttendanceService attendance)
{
    public AttendanceService Service => attendance;

    private AttendanceSettings State => settings.Current.Attendance;

    /// <summary>
    /// Mindent ellenőriz, ami a beíráshoz kell, és szükség esetén kérdez.
    /// Null, ha a felhasználó megszakította vagy hiba történt (ilyenkor már tájékoztattuk).
    /// </summary>
    public async Task<AttendanceTarget?> PrepareAsync(IDialogService dialogs)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        var path = await ResolvePdfPathAsync(dialogs, today);
        if (path is null)
        {
            return null;
        }

        IReadOnlyList<string> names;
        try
        {
            names = await Task.Run(() => attendance.GetFieldNames(path));
        }
        catch (AttendanceException ex)
        {
            await dialogs.ShowMessageAsync("A PDF nem olvasható", ex.Message);
            return null;
        }

        if (names.Count == 0)
        {
            await dialogs.ShowMessageAsync("Nincs kitölthető mező", "Ebben a PDF-ben nincsenek kitölthető mezők. Biztosan a jelenléti ívet választottad?");
            return null;
        }

        var set = names.ToHashSet(StringComparer.Ordinal);
        if (State.Templates is { } existing)
        {
            var repaired = AttendanceService.Repair(existing, set);
            if (repaired.DailyTemplates.All(t => FieldTemplate.Score(t, set) >= 2))
            {
                if (repaired != existing)
                {
                    Log.Info($"Mezősablonok javítva: {existing} -> {repaired}");
                    settings.Update(s => s.Attendance.Templates = repaired);
                }

                return new AttendanceTarget(path, repaired);
            }
        }

        var templates = await ConfigureFieldsAsync(dialogs, names, today.Day, allowAutomatic: true);
        return templates is null ? null : new AttendanceTarget(path, templates);
    }

    /// <summary>Új PDF választása (pl. a beállításokból). A mezőket is újra beállítja.</summary>
    public async Task<bool> ChooseNewPdfAsync(IDialogService dialogs)
    {
        var path = await dialogs.PickPdfAsync();
        if (path is null)
        {
            return false;
        }

        settings.Update(s => s.Attendance.PdfPath = path);
        return await ReconfigureFieldsAsync(dialogs);
    }

    public async Task<bool> ReconfigureFieldsAsync(IDialogService dialogs)
    {
        if (State.PdfPath is not { } path || !await Task.Run(() => File.Exists(path)))
        {
            return await ChooseNewPdfAsync(dialogs);
        }

        try
        {
            var names = await Task.Run(() => attendance.GetFieldNames(path));
            return await ConfigureFieldsAsync(dialogs, names, DateTime.Now.Day, allowAutomatic: true) is not null;
        }
        catch (AttendanceException ex)
        {
            await dialogs.ShowMessageAsync("A PDF nem olvasható", ex.Message);
            return false;
        }
    }

    private async Task<string?> ResolvePdfPathAsync(IDialogService dialogs, DateOnly today)
    {
        var path = State.PdfPath;

        if (path is not null && !AttendancePdfLocator.IsForMonth(path, today) && AttendancePdfLocator.DetectPeriod(path) is not null)
        {
            var suggestion = AttendancePdfLocator.SuggestPathFor(path, today);
            var monthName = AttendancePdfLocator.MonthNames[today.Month - 1];
            if (suggestion is not null && await Task.Run(() => File.Exists(suggestion)))
            {
                var choice = await dialogs.ChooseAsync(
                    "Új hónap",
                    $"A beállított jelenléti ív egy korábbi hónapé. Megtaláltam az ideit ({monthName}):\n\n{Path.GetFileName(suggestion)}\n\nÁtváltsak rá?",
                    "Átváltok", "Másik PDF");
                switch (choice)
                {
                    case DialogChoice.Primary:
                        settings.Update(s => s.Attendance.PdfPath = suggestion);
                        return suggestion;
                    case DialogChoice.Secondary:
                        return await PickAndStoreAsync(dialogs);
                    default:
                        return null;
                }
            }

            var pick = await dialogs.ChooseAsync(
                "Új hónap",
                $"A beállított jelenléti ív egy korábbi hónapé, a(z) {monthName} havi ív pedig még nem található itt:\n\n{suggestion ?? "(ismeretlen hely)"}\n\nVálaszd ki az aktuális ívet.",
                "Kiválasztás", "A régi marad");
            if (pick == DialogChoice.Primary)
            {
                return await PickAndStoreAsync(dialogs);
            }

            if (pick == DialogChoice.None)
            {
                return null;
            }
        }

        if (path is null)
        {
            var ok = await dialogs.ConfirmAsync(
                "Jelenléti ív",
                "A munkaidő a jelenléti ív PDF-jébe kerül beírásra. Válaszd ki a havi jelenléti ívet.",
                "Kiválasztás");
            return ok ? await PickAndStoreAsync(dialogs) : null;
        }

        if (!await Task.Run(() => File.Exists(path)))
        {
            var choice = await dialogs.ChooseAsync(
                "A PDF nem érhető el",
                $"A jelenléti ív nem található (vagy a hálózati meghajtó nem elérhető):\n\n{path}",
                "Másik PDF", null);
            return choice == DialogChoice.Primary ? await PickAndStoreAsync(dialogs) : null;
        }

        return path;
    }

    private async Task<string?> PickAndStoreAsync(IDialogService dialogs)
    {
        var picked = await dialogs.PickPdfAsync();
        if (picked is not null)
        {
            settings.Update(s => s.Attendance.PdfPath = picked);
        }

        return picked;
    }

    private async Task<AttendanceTemplates?> ConfigureFieldsAsync(IDialogService dialogs, IReadOnlyList<string> names, int day, bool allowAutomatic)
    {
        if (allowAutomatic && AttendanceFieldDetector.Detect(names, day) is { } detected)
        {
            var preview =
                $"Érkezés:  {FieldTemplate.Resolve(detected.Arrival, day)}\n" +
                $"Távozás:  {FieldTemplate.Resolve(detected.Leave, day)}\n" +
                $"Óraszám:  {FieldTemplate.Resolve(detected.Hours, day)}\n" +
                $"Aláírás:  {FieldTemplate.Resolve(detected.Signature, day)}\n" +
                $"Havi összesítés:  {detected.TotalHours ?? "(nincs)"}";
            var choice = await dialogs.ChooseAsync(
                "Mezők felismerve",
                $"A mai napra ({day}.) ezeket a mezőket találtam:\n\n{preview}\n\nHasználjam ezeket?",
                "Igen", "Kézzel");
            if (choice == DialogChoice.None)
            {
                return null;
            }

            if (choice == DialogChoice.Primary)
            {
                settings.Update(s => s.Attendance.Templates = detected);
                return detected;
            }
        }

        var set = names.ToHashSet(StringComparer.Ordinal);
        var sorted = names.OrderBy(n => n, NaturalStringComparer.Instance).ToList();

        async Task<string?> Pick(string label, string what)
        {
            var field = await dialogs.PickFieldAsync(label, $"Válaszd ki a(z) {what} mezőt a mai nap ({day}.) sorában:", sorted, optional: false);
            if (field is null)
            {
                return null;
            }

            return FieldTemplate.Derive(field, day, set)?.Template ?? field;
        }

        var arrival = await Pick("Érkezés", "ÉRKEZÉS");
        if (arrival is null) return null;
        var leave = await Pick("Távozás", "TÁVOZÁS");
        if (leave is null) return null;
        var hours = await Pick("Óraszám", "NAPPALI ÓRASZÁM");
        if (hours is null) return null;
        var sign = await Pick("Aláírás", "ALÁÍRÁS");
        if (sign is null) return null;

        var total = await dialogs.PickFieldAsync(
            "Havi összesítés (opcionális)",
            "Válaszd ki a HAVI ÖSSZESÍTETT ÓRASZÁM mezőt, vagy hagyd ki.",
            sorted, optional: true);

        var templates = new AttendanceTemplates(arrival, leave, hours, sign, total);
        settings.Update(s => s.Attendance.Templates = templates);
        return templates;
    }
}

/// <summary>„Természetes” rendezés: ALÁÍRÁS2 &lt; ALÁÍRÁS10.</summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var si = i;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                var sj = j;
                while (j < y.Length && char.IsDigit(y[j])) j++;
                var a = x.AsSpan(si, i - si).TrimStart('0');
                var b = y.AsSpan(sj, j - sj).TrimStart('0');
                if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                var c = a.SequenceCompareTo(b);
                if (c != 0) return c;
            }
            else
            {
                var c = string.Compare(x, i, y, j, 1, StringComparison.CurrentCultureIgnoreCase);
                if (c != 0) return c;
                i++;
                j++;
            }
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }
}
