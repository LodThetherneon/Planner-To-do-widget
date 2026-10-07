namespace PlannerWidget.Core.Attendance;

public sealed record ArrivalResult(string ClockText, string FieldName, AttendanceTemplates Templates);

public sealed record DepartureResult(
    string ClockText,
    int Hours,
    string? MonthlyTotal,
    AttendanceTemplates Templates);

/// <summary>
/// „Munka kezdete / vége” logika: a jelenléti ív napi sorának kitöltése.
/// Minden művelet előtt ellenőrzi (és szükség esetén javítja) a mezőnév-sablonokat
/// a PDF valódi mezőlistája alapján – a javított sablont visszaadja, hogy el lehessen menteni.
/// </summary>
public sealed class AttendanceService(IPdfFormService pdf)
{
    public IReadOnlyList<string> GetFieldNames(string path) => pdf.ReadFields(path).Keys.ToList();

    public static AttendanceTemplates Repair(AttendanceTemplates templates, IReadOnlySet<string> names) =>
        templates with
        {
            Arrival = FieldTemplate.Repair(templates.Arrival, names),
            Leave = FieldTemplate.Repair(templates.Leave, names),
            Hours = FieldTemplate.Repair(templates.Hours, names),
            Signature = FieldTemplate.Repair(templates.Signature, names),
            TotalHours = templates.TotalHours is { } total && names.Contains(total)
                ? total
                : AttendanceFieldDetector.DetectTotal(names),
        };

    public ArrivalResult RecordArrival(string path, AttendanceTemplates templates, DateTime start)
    {
        var fields = pdf.ReadFields(path);
        var names = fields.Keys.ToHashSet(StringComparer.Ordinal);
        templates = Repair(templates, names);

        var field = RequireField(templates.Arrival, start.Day, names, "érkezés");
        var clock = WorkTime.FormatClock(WorkTime.RoundToNearestHour(start));
        pdf.WriteFields(path, new Dictionary<string, string> { [field] = clock });
        return new ArrivalResult(clock, field, templates);
    }

    public DepartureResult RecordDeparture(
        string path,
        AttendanceTemplates templates,
        DateTime start,
        DateTime end,
        string? signatureName)
    {
        var fields = pdf.ReadFields(path);
        var names = fields.Keys.ToHashSet(StringComparer.Ordinal);
        templates = Repair(templates, names);

        var day = end.Day;
        var leaveField = RequireField(templates.Leave, day, names, "távozás");
        var hoursField = RequireField(templates.Hours, day, names, "óraszám");
        var hours = WorkTime.ComputeHours(start, end);
        var clock = WorkTime.FormatClock(WorkTime.RoundToNearestHour(end));

        var values = new Dictionary<string, string>
        {
            [leaveField] = clock,
            [hoursField] = hours.ToString(),
        };

        if (!string.IsNullOrWhiteSpace(signatureName))
        {
            values[RequireField(templates.Signature, day, names, "aláírás")] = signatureName.Trim();
        }

        string? total = null;
        if (templates.TotalHours is { } totalField && names.Contains(totalField))
        {
            double sum = hours;
            for (var d = 1; d <= 31; d++)
            {
                if (d != day && fields.TryGetValue(FieldTemplate.Resolve(templates.Hours, d), out var raw))
                {
                    sum += WorkTime.ParseHours(raw);
                }
            }

            total = WorkTime.FormatHours(sum);
            values[totalField] = total;
        }

        pdf.WriteFields(path, values);
        return new DepartureResult(clock, hours, total, templates);
    }

    private static string RequireField(string template, int day, IReadOnlySet<string> names, string label)
    {
        var field = FieldTemplate.Resolve(template, day);
        if (!names.Contains(field))
        {
            throw new AttendanceException(
                $"A(z) {label} mező ({field}) nem található a PDF-ben. Állítsd be újra a mezőket a beállításokban.");
        }

        return field;
    }
}
