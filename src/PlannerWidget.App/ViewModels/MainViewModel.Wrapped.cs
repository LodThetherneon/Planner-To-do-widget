using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlannerWidget.Core.Wrapped;

namespace PlannerWidget.App.ViewModels;

/// <summary>Egy sor a havi összegzésben: nagy szám + felirat + kis részlet.</summary>
public sealed record WrappedLine(string Value, string Label, string Detail)
{
    public bool HasDetail => Detail.Length > 0;
}

/// <summary>
/// Havi „Wrapped”: a profilképre kattintva nyílik. Új hónap elején elkészül az előzőé, és a profilkép monogramja
/// helyett „W” látszik piros körrel, amíg meg nem nyitják.
/// </summary>
public sealed partial class MainViewModel
{
    private static readonly CultureInfo Hu = CultureInfo.GetCultureInfo("hu-HU");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileInitials))]
    public partial bool WrappedHasUnseen { get; set; }

    [ObservableProperty]
    public partial string WrappedTitle { get; set; } = "";

    [ObservableProperty]
    public partial string WrappedEmptyText { get; set; } = "";

    [ObservableProperty]
    public partial bool WrappedHasReport { get; set; }

    [ObservableProperty]
    public partial bool WrappedCanPrev { get; set; }

    [ObservableProperty]
    public partial bool WrappedCanNext { get; set; }

    public ObservableCollection<WrappedLine> WrappedLines { get; } = [];

    public string ProfileInitials => WrappedHasUnseen ? "W" : UserInitials;

    private int _wrappedIndex = -1;

    /// <summary>Frissítés után: feljegyzés, esetleges új hónapi összegzés.</summary>
    private void UpdateWrapped(IReadOnlyList<Core.Models.PlannerTask> tasks)
    {
        if (_services.IsDemo)
        {
            _services.Wrapped.SeedDemoHistory();
        }

        _services.Wrapped.Update(tasks, _services.Settings.Current.Tickets.PlanId);
        WrappedHasUnseen = _services.Wrapped.HasUnseen;
    }

    /// <summary>A profilkép megnyitja az összegzést (a legutóbbi hónappal); ilyenkor a piros jelzés eltűnik.</summary>
    public void OpenWrapped()
    {
        var reports = _services.Wrapped.Reports;
        ShowWrapped(reports.Count - 1);
        _services.Wrapped.MarkSeen();
        WrappedHasUnseen = false;
    }

    [RelayCommand]
    private void WrappedPrev() => ShowWrapped(_wrappedIndex - 1);

    [RelayCommand]
    private void WrappedNext() => ShowWrapped(_wrappedIndex + 1);

    private void ShowWrapped(int index)
    {
        var reports = _services.Wrapped.Reports;
        WrappedLines.Clear();
        if (reports.Count == 0)
        {
            _wrappedIndex = -1;
            WrappedHasReport = false;
            WrappedCanPrev = WrappedCanNext = false;
            WrappedTitle = "Havi összegzés";
            WrappedEmptyText = "Még nincs elkészült összegzés. Minden hónap elsején elkészül az előző hónapé – ha addig " +
                               "használod a widgetet, itt találod.";
            return;
        }

        _wrappedIndex = Math.Clamp(index, 0, reports.Count - 1);
        var r = reports[_wrappedIndex];
        WrappedHasReport = true;
        WrappedCanPrev = _wrappedIndex > 0;
        WrappedCanNext = _wrappedIndex < reports.Count - 1;

        var month = DateTime.ParseExact(r.Month, "yyyy-MM", CultureInfo.InvariantCulture);
        WrappedTitle = $"{month.ToString("yyyy. MMMM", Hu)} összegzése";

        WrappedLines.Add(new(Ratio(r.TasksArrivedDone, r.TasksArrived), "feladat érkezett – ennyi lett kész",
            Percent(r.TasksArrivedDone, r.TasksArrived)));
        WrappedLines.Add(new(Ratio(r.TicketsArrivedDone, r.TicketsArrived), "ügy érkezett – ennyi lett kész",
            Percent(r.TicketsArrivedDone, r.TicketsArrived)));
        WrappedLines.Add(new(r.TotalCompleted.ToString(Hu), "lezárt feladat és ügy összesen", ""));
        if (r.OldestOpenTitle is not null)
        {
            WrappedLines.Add(new($"{r.OldestOpenDays} nap", "a leghosszabb ideje nyitott feladat", r.OldestOpenTitle));
        }

        if (r.FastestTitle is not null)
        {
            var span = r.FastestHours < 1 ? "1 óránál kevesebb"
                : r.FastestHours < 48 ? $"{Math.Round(r.FastestHours):0} óra"
                : $"{Math.Round(r.FastestHours / 24):0} nap";
            WrappedLines.Add(new(span, "a leggyorsabban elintézett", r.FastestTitle));
        }

        if (r.BusiestDay is { } day)
        {
            WrappedLines.Add(new($"{r.BusiestDayCount} db", "a legtermékenyebb nap",
                day.ToString("MMMM d., dddd", Hu)));
        }
    }

    private static string Ratio(int done, int total) => $"{done} / {total}";

    private static string Percent(int done, int total) =>
        total == 0 ? "" : $"{Math.Round(100.0 * done / total):0}%";
}
