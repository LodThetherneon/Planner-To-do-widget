using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlannerWidget.App.Services;
using PlannerWidget.Core.Attendance;
using PlannerWidget.Core.Logging;

namespace PlannerWidget.App.ViewModels;

/// <summary>Munkaidő mérése és a jelenléti ív kitöltése („Munka kezdete / vége”).</summary>
public sealed partial class MainViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkButtonText), nameof(WorkButtonLabel), nameof(WorkButtonGlyph), nameof(WorkToolTip))]
    public partial bool IsWorkRunning { get; set; }

    [ObservableProperty]
    public partial bool IsWorkBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkButtonLabel))]
    public partial string WorkElapsedText { get; set; } = "";

    /// <summary>A gomb akadálymentes neve és a tálcamenü szövege.</summary>
    public string WorkButtonText => IsWorkRunning ? "Munka vége" : "Munka kezdete";

    /// <summary>A gombon látható felirat: futás közben az eltelt idővel.</summary>
    public string WorkButtonLabel => IsWorkRunning && WorkElapsedText.Length > 0 ? $"Vége · {WorkElapsedText}" : WorkButtonText;

    /// <summary>Segoe Fluent ikon: Stop / Play.</summary>
    public string WorkButtonGlyph => IsWorkRunning ? "\uE71A" : "\uE768";

    public string WorkToolTip => IsWorkRunning && WorkStart is { } start
        ? $"Kezdés: {start:HH:mm} (a jelenléti ívben: {WorkTime.FormatClock(WorkTime.RoundToNearestHour(start))})"
        : "Munkaidő indítása és beírása a jelenléti ívbe";

    private DateTime? WorkStart => _services.Settings.Current.Attendance.WorkStart;

    private void RestoreWorkState()
    {
        var state = _services.Settings.Current.Attendance;
        if (state.WorkRunning && state.WorkStart is null)
        {
            _services.Settings.Update(s => s.Attendance.WorkRunning = false);
        }

        IsWorkRunning = state.WorkRunning && state.WorkStart is not null;
        UpdateWorkElapsed();
    }

    private void UpdateWorkElapsed()
    {
        WorkElapsedText = IsWorkRunning && WorkStart is { } start
            ? WorkTime.FormatDuration(DateTime.Now - start)
            : "";
        OnPropertyChanged(nameof(WorkToolTip));
        TrayTooltipChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ToggleWorkAsync()
    {
        if (IsWorkBusy)
        {
            return;
        }

        IsWorkBusy = true;
        try
        {
            if (IsWorkRunning)
            {
                await StopWorkAsync();
            }
            else
            {
                await StartWorkAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Munkaidő-kezelési hiba", ex);
            ShowError("Munkaidő", ex.Message);
        }
        finally
        {
            IsWorkBusy = false;
            UpdateWorkElapsed();
        }
    }

    private async Task StartWorkAsync()
    {
        var target = await _services.Attendance.PrepareAsync(_dialogs);
        if (target is null)
        {
            return;
        }

        var now = DateTime.Now;
        try
        {
            var result = await Task.Run(() => _services.Attendance.Service.RecordArrival(target.PdfPath, target.Templates, now));
            PersistTemplates(target.Templates, result.Templates);
            ShowStatus($"Munka kezdete: {result.ClockText} – beírva ✓", StatusKind.Success, TimeSpan.FromSeconds(4));
        }
        catch (AttendanceException ex)
        {
            var choice = await _dialogs.ChooseAsync(
                "Nem sikerült beírni",
                ex.Message + "\n\nElindítsam a munkaidő mérését beírás nélkül? (Később kézzel pótolhatod.)",
                "Indítás így", null);
            if (choice != DialogChoice.Primary)
            {
                return;
            }
        }

        _services.Settings.Update(s =>
        {
            s.Attendance.WorkRunning = true;
            s.Attendance.WorkStart = now;
        });
        IsWorkRunning = true;
    }

    private async Task StopWorkAsync()
    {
        var start = WorkStart ?? DateTime.Now;
        var end = DateTime.Now;
        var hours = WorkTime.ComputeHours(start, end);
        var from = WorkTime.FormatClock(WorkTime.RoundToNearestHour(start));
        var to = WorkTime.FormatClock(WorkTime.RoundToNearestHour(end));

        var choice = await _dialogs.ChooseAsync(
            "Munka vége",
            $"Ledolgozott idő: {WorkTime.FormatDuration(end - start)}\n\nA jelenléti ívbe kerül:\n{from} – {to}, {hours} óra\n\nBeírjam?\n\n(„Csak leállítás”: a munkaidő leáll, de a PDF nem változik.)",
            "Beírás", "Csak leállítás");

        if (choice == DialogChoice.None)
        {
            return;
        }

        if (choice == DialogChoice.Primary)
        {
            var target = await _services.Attendance.PrepareAsync(_dialogs);
            if (target is null)
            {
                return; // A munkaidő tovább fut, később újrapróbálható.
            }

            string? signature = null;
            try
            {
                signature = (await _services.Planner.GetMeAsync()).DisplayName;
            }
            catch (Exception ex)
            {
                Log.Warn("Az aláíráshoz szükséges név nem kérhető le", ex);
                signature = UserDisplayName;
            }

            try
            {
                var result = await Task.Run(() =>
                    _services.Attendance.Service.RecordDeparture(target.PdfPath, target.Templates, start, end, signature));
                PersistTemplates(target.Templates, result.Templates);
                var total = result.MonthlyTotal is null ? "" : $", havi összesen {result.MonthlyTotal} óra";
                ShowStatus($"Munka vége: {result.ClockText} ({result.Hours} óra{total}) ✓", StatusKind.Success, TimeSpan.FromSeconds(6));
            }
            catch (AttendanceException ex)
            {
                await _dialogs.ShowMessageAsync("Nem sikerült beírni", ex.Message + "\n\nA munkaidő tovább fut, próbáld újra a PDF bezárása után.");
                return;
            }
        }

        _services.Settings.Update(s =>
        {
            s.Attendance.WorkRunning = false;
            s.Attendance.WorkStart = null;
        });
        IsWorkRunning = false;
    }

    private void PersistTemplates(AttendanceTemplates before, AttendanceTemplates after)
    {
        if (before != after)
        {
            _services.Settings.Update(s => s.Attendance.Templates = after);
        }
    }
}
