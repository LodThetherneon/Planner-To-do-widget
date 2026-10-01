using System.Globalization;
using PlannerWidget.Core.Models;

namespace PlannerWidget.Core.Tasks;

/// <summary>A határidőhöz viszonyított állapot – ez adja a kártya színét is.</summary>
public enum DueState
{
    Overdue,
    DueToday,
    DueSoon,
    Later,
    NoDueDate,
    Completed,
}

public sealed record TaskCounts(int Active, int Overdue, int DueToday);

public static class TaskOrdering
{
    /// <summary>Ennyi napon belüli határidő számít „hamarosan esedékesnek” (sárga).</summary>
    public const int SoonDays = 7;

    public static DueState GetDueState(PlannerTask task, DateOnly today)
    {
        if (task.IsCompleted)
        {
            return DueState.Completed;
        }

        if (task.DueDate is not { } due)
        {
            return DueState.NoDueDate;
        }

        var diff = due.DayNumber - today.DayNumber;
        return diff switch
        {
            < 0 => DueState.Overdue,
            0 => DueState.DueToday,
            <= SoonDays => DueState.DueSoon,
            _ => DueState.Later,
        };
    }

    /// <summary>
    /// Aktív feladatok sorrendje: lejárt → mai/hamarosan → később → határidő nélkül;
    /// csoporton belül határidő, majd prioritás, majd cím szerint.
    /// </summary>
    public static IReadOnlyList<PlannerTask> SortActive(IEnumerable<PlannerTask> tasks, DateOnly today) =>
        tasks.Where(t => !t.IsCompleted)
            .OrderBy(t => GroupOf(GetDueState(t, today)))
            .ThenBy(t => t.DueDate?.DayNumber ?? int.MaxValue)
            .ThenBy(t => (int)t.PriorityLevel)
            .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Kész feladatok: a legutóbb befejezett elöl.</summary>
    public static IReadOnlyList<PlannerTask> SortCompleted(IEnumerable<PlannerTask> tasks) =>
        tasks.Where(t => t.IsCompleted)
            .OrderByDescending(t => t.CompletedDateTime ?? DateTimeOffset.MinValue)
            .ThenByDescending(t => t.DueDate?.DayNumber ?? int.MinValue)
            .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static TaskCounts Count(IEnumerable<PlannerTask> tasks, DateOnly today)
    {
        int active = 0, overdue = 0, dueToday = 0;
        foreach (var task in tasks)
        {
            switch (GetDueState(task, today))
            {
                case DueState.Completed:
                    continue;
                case DueState.Overdue:
                    overdue++;
                    break;
                case DueState.DueToday:
                    dueToday++;
                    break;
            }

            active++;
        }

        return new TaskCounts(active, overdue, dueToday);
    }

    private static int GroupOf(DueState state) => state switch
    {
        DueState.Overdue => 0,
        DueState.DueToday or DueState.DueSoon => 1,
        DueState.Later => 2,
        _ => 3,
    };
}

/// <summary>Határidők emberi, magyar nyelvű megjelenítése.</summary>
public static class DueDateFormatter
{
    private static readonly CultureInfo Hungarian = CultureInfo.GetCultureInfo("hu-HU");

    public static string Format(DateOnly? due, DateOnly today)
    {
        if (due is not { } d)
        {
            return "Nincs határidő";
        }

        var diff = d.DayNumber - today.DayNumber;
        return diff switch
        {
            0 => "Ma",
            1 => "Holnap",
            -1 => "Tegnap lejárt",
            < -1 => $"{-diff} napja lejárt",
            < 7 => Capitalize(d.ToString("dddd", Hungarian)),
            _ => FormatDate(d, today),
        };
    }

    /// <summary>Rövid dátum: „márc. 14.” (más évben évszámmal).</summary>
    public static string FormatDate(DateOnly date, DateOnly today) =>
        date.Year == today.Year
            ? date.ToString("MMM d.", Hungarian)
            : date.ToString("yyyy. MMM d.", Hungarian);

    public static string FormatLong(DateOnly date) => date.ToString("yyyy. MMMM d., dddd", Hungarian);

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], Hungarian) + s[1..];
}
