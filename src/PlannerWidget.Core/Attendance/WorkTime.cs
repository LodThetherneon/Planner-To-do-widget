using System.Globalization;

namespace PlannerWidget.Core.Attendance;

/// <summary>
/// Munkaidő-számítás a jelenléti ívhez. A szabály a régi verzióval azonos:
/// érkezés és távozás a legközelebbi egész órára kerekítve, az óraszám a kerekített értékek különbsége.
/// </summary>
public static class WorkTime
{
    public static DateTime RoundToNearestHour(DateTime value)
    {
        var floored = new DateTime(value.Year, value.Month, value.Day, value.Hour, 0, 0, value.Kind);
        return value.Minute >= 30 ? floored.AddHours(1) : floored;
    }

    public static int ComputeHours(DateTime start, DateTime end)
    {
        var hours = (RoundToNearestHour(end) - RoundToNearestHour(start)).TotalHours;
        return Math.Max(0, (int)Math.Round(hours, MidpointRounding.AwayFromZero));
    }

    /// <summary>„9:00” formátum – a jelenléti ív meglévő bejegyzéseivel egyezően.</summary>
    public static string FormatClock(DateTime value) => value.ToString("H:mm", CultureInfo.InvariantCulture);

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var totalMinutes = (int)duration.TotalMinutes;
        return totalMinutes < 60 ? $"{totalMinutes} perc" : $"{totalMinutes / 60} ó {totalMinutes % 60:00} p";
    }

    /// <summary>Óraszám értelmezése a PDF-ből („7”, „7,5”, „7.5 óra”).</summary>
    public static double ParseHours(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        var cleaned = new string(raw.Replace(',', '.').Where(c => char.IsDigit(c) || c == '.').ToArray());
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    public static string FormatHours(double hours) =>
        Math.Abs(hours % 1) < 0.0001
            ? ((long)Math.Round(hours)).ToString(CultureInfo.InvariantCulture)
            : hours.ToString("0.##", CultureInfo.InvariantCulture);
}
