using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlannerWidget.App.Services;
using PlannerWidget.Core.Attendance;
using PlannerWidget.Core.Logging;
using PlannerWidget.Core.Settings;
using PlannerWidget.Core.Storage;
using PlannerWidget.Core.Tickets;

namespace PlannerWidget.App.ViewModels;

/// <summary>
/// A Beállítások ablak nézetmodellje. Minden változás azonnal mentődik és érvénybe lép; a Mentés gomb
/// ezen felül mindent egyszerre ír ki, újraalkalmaz, frissíti a feladatlistát és bezárja az ablakot.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogService _dialogs;
    private readonly MainViewModel _main;
    private bool _loading;
    private bool _suppressTicketPlan;

    public SettingsViewModel(AppServices services, IDialogService dialogs, MainViewModel main)
    {
        _services = services;
        _dialogs = dialogs;
        _main = main;
        Load();
        _ = LoadTicketPlansAsync();
    }

    public static IReadOnlyList<string> ThemeOptions { get; } = ["Rendszer szerint", "Sötét", "Világos"];
    public static IReadOnlyList<string> HotkeyOptions { get; } = Hotkey.Presets;
    public static IReadOnlyList<string> CloseOptions { get; } = ["Kilép a programból", "Elrejti (a tálcán fut tovább)"];

    [ObservableProperty] public partial int ThemeIndex { get; set; }
    [ObservableProperty] public partial bool AlwaysOnTop { get; set; }
    [ObservableProperty] public partial bool BlurWhenActive { get; set; }
    [ObservableProperty] public partial bool BlurWhenInactive { get; set; }
    [ObservableProperty] public partial double RefreshIntervalMinutes { get; set; }
    [ObservableProperty] public partial double ExpandedHeight { get; set; }
    [ObservableProperty] public partial double WindowWidth { get; set; }
    [ObservableProperty] public partial bool SoundsEnabled { get; set; }
    [ObservableProperty] public partial bool NotificationsEnabled { get; set; }
    [ObservableProperty] public partial bool ConfirmDelete { get; set; }
    [ObservableProperty] public partial int HotkeyIndex { get; set; }
    [ObservableProperty] public partial int CloseButtonIndex { get; set; }
    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty] public partial bool UseWindowsAccount { get; set; }
    [ObservableProperty] public partial string AccountText { get; set; } = "";
    [ObservableProperty] public partial string PdfPathText { get; set; } = "";
    [ObservableProperty] public partial string TemplatesText { get; set; } = "";
    [ObservableProperty] public partial string WorkStateText { get; set; } = "";

    // Beérkező ügyek
    public ObservableCollection<PlanOption> TicketPlanOptions { get; } = [];
    [ObservableProperty] public partial int TicketPlanIndex { get; set; } = -1;
    [ObservableProperty] public partial string TicketPlanStatus { get; set; } = "";
    [ObservableProperty] public partial bool NotifyNewTickets { get; set; }
    [ObservableProperty] public partial bool ConfirmCloseTicket { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TicketUrlWarning))]
    public partial string TicketUrlTemplate { get; set; } = "";

    public string TicketUrlWarning => TicketLinks.IsValidTemplate(TicketUrlTemplate)
        ? ""
        : $"A sablonban legyen {TicketLinks.Placeholder}, és teljes http(s) címet adjon. Amíg ez nem teljesül, a korábbi sablon marad érvényben.";

    private static PlanOption NoTicketPlan { get; } = new(null, "Nincs");

    public string VersionText { get; } =
        "Planner Widget " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "") + " · WinUI 3 / .NET 10";

    public string DataFolder => AppPaths.Root;

    public static double MinWindowWidth => AppSettings.MinWindowWidth;
    public static double MaxWindowWidth => AppSettings.MaxWindowWidth;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAndApplyCommand))]
    public partial bool IsSaving { get; set; }

    /// <summary>A Mentés gomb után: az ablak bezárható, a widget előhozható.</summary>
    public event EventHandler? Saved;

    public void Load()
    {
        _loading = true;
        var s = _services.Settings.Current;
        ThemeIndex = (int)s.Theme;
        AlwaysOnTop = s.AlwaysOnTop;
        BlurWhenActive = s.BlurWhenActive;
        BlurWhenInactive = s.BlurWhenInactive;
        RefreshIntervalMinutes = s.RefreshIntervalMinutes;
        ExpandedHeight = s.ExpandedHeight;
        WindowWidth = s.WindowWidth;
        SoundsEnabled = s.SoundsEnabled;
        NotificationsEnabled = s.NotificationsEnabled;
        ConfirmDelete = s.ConfirmDelete;
        HotkeyIndex = Math.Max(0, Array.IndexOf(Hotkey.Presets, s.Hotkey));
        CloseButtonIndex = (int)s.CloseButton;
        UseWindowsAccount = s.AuthMode == AuthMode.Automatic;
        try
        {
            StartWithWindows = _services.Autostart.IsEnabled;
        }
        catch (Exception ex)
        {
            Log.Warn("Az automatikus indítás állapota nem olvasható", ex);
        }

        AccountText = _main.IsSignedIn
            ? $"Bejelentkezve: {_main.UserDisplayName ?? _services.Auth.AccountName}" +
              (_services.Auth.AccountName is { } upn ? $" ({upn})" : "")
            : "Nincs bejelentkezve";

        var tk = s.Tickets;
        NotifyNewTickets = tk.NotifyNewTickets;
        ConfirmCloseTicket = tk.ConfirmCloseTicket;
        TicketUrlTemplate = tk.UrlTemplate;
        if (TicketPlanOptions.Count == 0)
        {
            SetTicketPlanOptions([]);
        }

        var a = s.Attendance;
        PdfPathText = a.PdfPath ?? "Nincs kiválasztva";
        TemplatesText = a.Templates is { } t
            ? $"Érkezés: {t.Arrival}\nTávozás: {t.Leave}\nÓraszám: {t.Hours}\nAláírás: {t.Signature}\nÖsszesítés: {t.TotalHours ?? "–"}"
            : "Még nincs beállítva (az első „Munka kezdete” felismeri).";
        WorkStateText = a.WorkRunning && a.WorkStart is { } start
            ? $"Fut: {start:yyyy.MM.dd. HH:mm} óta ({WorkTime.FormatDuration(DateTime.Now - start)})"
            : "Nem fut";
        _loading = false;
    }

    private void Save(Action<AppSettings> change)
    {
        if (!_loading)
        {
            _services.Settings.Update(change);
        }
    }

    partial void OnThemeIndexChanged(int value) => Save(s => s.Theme = (AppTheme)Math.Clamp(value, 0, 2));
    partial void OnAlwaysOnTopChanged(bool value) => Save(s => s.AlwaysOnTop = value);
    partial void OnBlurWhenActiveChanged(bool value) => Save(s => s.BlurWhenActive = value);
    partial void OnBlurWhenInactiveChanged(bool value) => Save(s => s.BlurWhenInactive = value);
    partial void OnSoundsEnabledChanged(bool value) => Save(s => s.SoundsEnabled = value);
    partial void OnNotificationsEnabledChanged(bool value) => Save(s => s.NotificationsEnabled = value);
    partial void OnConfirmDeleteChanged(bool value) => Save(s => s.ConfirmDelete = value);
    partial void OnCloseButtonIndexChanged(int value) => Save(s => s.CloseButton = (CloseButtonBehavior)Math.Clamp(value, 0, 1));
    partial void OnUseWindowsAccountChanged(bool value) => Save(s => s.AuthMode = value ? AuthMode.Automatic : AuthMode.Browser);

    partial void OnNotifyNewTicketsChanged(bool value) => Save(s => s.Tickets.NotifyNewTickets = value);
    partial void OnConfirmCloseTicketChanged(bool value) => Save(s => s.Tickets.ConfirmCloseTicket = value);

    partial void OnTicketPlanIndexChanged(int value)
    {
        if (_loading || _suppressTicketPlan || value < 0 || value >= TicketPlanOptions.Count)
        {
            return;
        }

        var planId = TicketPlanOptions[value].Id;
        if (planId == _services.Settings.Current.Tickets.PlanId)
        {
            return;
        }

        Save(s =>
        {
            s.Tickets.PlanId = planId;
            s.Tickets.NoPlan = planId is null;
        });
        Log.Info($"Ügyterv: {(planId is null ? "nincs" : TicketPlanOptions[value].Title)}");
        _main.OnTicketSettingsChanged();
    }

    partial void OnTicketUrlTemplateChanged(string value)
    {
        // Érvénytelen sablont nem mentünk, különben eltűnnének a linkek gépelés közben.
        if (_loading || !TicketLinks.IsValidTemplate(value) || value.Trim() == _services.Settings.Current.Tickets.UrlTemplate)
        {
            return;
        }

        Save(s => s.Tickets.UrlTemplate = value.Trim());
        _main.OnTicketSettingsChanged();
    }

    [RelayCommand]
    private void ResetTicketUrlTemplate() => TicketUrlTemplate = TicketLinks.DefaultUrlTemplate;

    /// <summary>Az ügyterv-választó feltöltése a felhasználó terveiből („Nincs” + név szerint).</summary>
    private async Task LoadTicketPlansAsync()
    {
        if (!_main.IsSignedIn)
        {
            TicketPlanStatus = "Bejelentkezés után a terveid közül választhatsz.";
            return;
        }

        TicketPlanStatus = "Tervek betöltése…";
        try
        {
            var plans = await _services.Planner.GetPlansAsync(_main.Tasks);
            SetTicketPlanOptions(plans.Select(p => new PlanOption(p.Id, p.Title)).ToList());
            TicketPlanStatus = "";
        }
        catch (Exception ex)
        {
            Log.Warn("Az ügyterv-választó tervei nem tölthetők be", ex);
            TicketPlanStatus = "A tervek listája most nem tölthető be; a mentett beállítás megmarad.";
        }
    }

    private void SetTicketPlanOptions(IReadOnlyList<PlanOption> plans)
    {
        var current = _services.Settings.Current.Tickets.PlanId;
        var options = new List<PlanOption> { NoTicketPlan };
        options.AddRange(plans);
        if (current is not null && options.All(o => o.Id != current))
        {
            // Offline vagy a terv már nem érhető el: a mentett értéket akkor is mutatjuk.
            var fallback = current == TicketSettings.DefaultPlanId ? TicketSettings.DefaultPlanTitle : "(ismeretlen terv)";
            options.Add(new PlanOption(current, _main.GetPlanTitle(current) ?? fallback));
        }

        _suppressTicketPlan = true;
        try
        {
            TicketPlanOptions.Clear();
            foreach (var option in options)
            {
                TicketPlanOptions.Add(option);
            }

            TicketPlanIndex = -1;
            TicketPlanIndex = Math.Max(0, options.FindIndex(o => o.Id == current));
        }
        finally
        {
            _suppressTicketPlan = false;
        }
    }

    partial void OnRefreshIntervalMinutesChanged(double value)
    {
        if (!double.IsNaN(value))
        {
            Save(s => s.RefreshIntervalMinutes = (int)Math.Round(value));
        }
    }

    partial void OnExpandedHeightChanged(double value)
    {
        if (!double.IsNaN(value))
        {
            Save(s => s.ExpandedHeight = value);
        }
    }

    partial void OnWindowWidthChanged(double value)
    {
        if (!double.IsNaN(value))
        {
            Save(s => s.WindowWidth = value);
        }
    }

    partial void OnHotkeyIndexChanged(int value)
    {
        if (value >= 0 && value < Hotkey.Presets.Length)
        {
            Save(s => s.Hotkey = Hotkey.Presets[value]);
        }
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        try
        {
            _services.Autostart.SetEnabled(value);
        }
        catch (Exception ex)
        {
            Log.Error("Automatikus indítás beállítása nem sikerült", ex);
            _ = _dialogs.ShowMessageAsync("Hiba", "Az automatikus indítás nem állítható be: " + ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveAndApply))]
    private async Task SaveAndApplyAsync()
    {
        IsSaving = true;
        try
        {
            _services.Settings.Update(s =>
            {
                s.Theme = (AppTheme)Math.Clamp(ThemeIndex, 0, 2);
                s.AlwaysOnTop = AlwaysOnTop;
                s.BlurWhenActive = BlurWhenActive;
                s.BlurWhenInactive = BlurWhenInactive;
                s.SoundsEnabled = SoundsEnabled;
                s.NotificationsEnabled = NotificationsEnabled;
                s.ConfirmDelete = ConfirmDelete;
                s.CloseButton = (CloseButtonBehavior)Math.Clamp(CloseButtonIndex, 0, 1);
                s.AuthMode = UseWindowsAccount ? AuthMode.Automatic : AuthMode.Browser;
                if (!double.IsNaN(RefreshIntervalMinutes)) s.RefreshIntervalMinutes = (int)Math.Round(RefreshIntervalMinutes);
                if (!double.IsNaN(ExpandedHeight)) s.ExpandedHeight = ExpandedHeight;
                if (!double.IsNaN(WindowWidth)) s.WindowWidth = WindowWidth;
                if (HotkeyIndex >= 0 && HotkeyIndex < Hotkey.Presets.Length) s.Hotkey = Hotkey.Presets[HotkeyIndex];
                if (TicketPlanIndex >= 0 && TicketPlanIndex < TicketPlanOptions.Count)
                {
                    s.Tickets.PlanId = TicketPlanOptions[TicketPlanIndex].Id;
                    s.Tickets.NoPlan = s.Tickets.PlanId is null;
                }

                if (TicketLinks.IsValidTemplate(TicketUrlTemplate)) s.Tickets.UrlTemplate = TicketUrlTemplate.Trim();
                s.Tickets.NotifyNewTickets = NotifyNewTickets;
                s.Tickets.ConfirmCloseTicket = ConfirmCloseTicket;
            });
            _main.OnTicketSettingsChanged();
            Log.Info("Beállítások mentve (Mentés gomb).");

            Saved?.Invoke(this, EventArgs.Empty);
            if (_main.IsSignedIn)
            {
                await _main.RefreshAsync(userInitiated: true);
            }

            _main.ShowStatus("Beállítások mentve ✓", StatusKind.Success);
        }
        catch (Exception ex)
        {
            Log.Error("Beállítások mentése", ex);
            await _dialogs.ShowMessageAsync("Hiba", "A beállítások nem menthetők: " + ex.Message);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanSaveAndApply() => !IsSaving;

    [RelayCommand]
    private async Task ChoosePdfAsync()
    {
        if (_services.Settings.Current.Attendance.WorkRunning)
        {
            await _dialogs.ShowMessageAsync("A munkaidő fut", "Előbb állítsd le a munkaidőt, mielőtt másik PDF-et választasz.");
            return;
        }

        await _services.Attendance.ChooseNewPdfAsync(_dialogs);
        Load();
    }

    [RelayCommand]
    private async Task ReconfigureFieldsAsync()
    {
        await _services.Attendance.ReconfigureFieldsAsync(_dialogs);
        Load();
    }

    [RelayCommand]
    private async Task ResetWorkAsync()
    {
        if (!_services.Settings.Current.Attendance.WorkRunning)
        {
            return;
        }

        if (await _dialogs.ConfirmAsync("Munkaidő leállítása", "A futó munkaidőt beírás nélkül leállítod?", "Leállítás", destructive: true))
        {
            _services.Settings.Update(s =>
            {
                s.Attendance.WorkRunning = false;
                s.Attendance.WorkStart = null;
            });
            _main.IsWorkRunning = false;
            Load();
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await _main.SignOutCommand.ExecuteAsync(null);
        Load();
    }

    [RelayCommand]
    private void OpenDataFolder() => Open(AppPaths.Root);

    [RelayCommand]
    private void OpenLog() => Open(Log.FilePath ?? AppPaths.LogDirectory);

    /// <summary>A régi Python-verzió (planner_defaults.json, plan_cache.json) beállításainak átvétele.</summary>
    [RelayCommand]
    private async Task ImportLegacyAsync()
    {
        var folder = await _dialogs.PickFolderAsync();
        if (folder is null)
        {
            return;
        }

        if (!LegacySettingsImporter.IsLegacyDirectory(folder))
        {
            await _dialogs.ShowMessageAsync("Nem található",
                $"Ebben a mappában nincs {LegacySettingsImporter.DefaultsFileName}. Válaszd a régi (Python) Planner widget mappáját.");
            return;
        }

        try
        {
            var titles = new Dictionary<string, string>();
            var result = LegacySettingsImporter.Import(folder, _services.Settings.Current, titles);
            _services.Settings.Save();
            _services.Planner.MergePlanTitles(titles);
            Log.Info($"Régi beállítások importálva innen: {folder} – {result}");
            await _dialogs.ShowMessageAsync("Importálás kész",
                $"Alapértelmezett oszlopok: {result.DefaultBuckets}\n" +
                $"Jelenléti ív útvonala: {(result.PdfPath ? "átvéve" : "már be volt állítva / nincs")}\n" +
                $"Mezőbeállítások: {(result.Templates ? "átvéve" : "már be voltak állítva / nincs")}\n" +
                $"Futó munkaidő: {(result.WorkState ? "átvéve" : "nem futott")}\n" +
                $"Tervnevek: {result.PlanTitles}\n\nA meglévő beállításaidat nem írta felül.");
            Load();
        }
        catch (Exception ex)
        {
            Log.Error("Régi beállítások importja", ex);
            await _dialogs.ShowMessageAsync("Hiba", "Az importálás nem sikerült: " + ex.Message);
        }
    }

    private static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Nem nyitható meg: {path}", ex);
        }
    }
}
