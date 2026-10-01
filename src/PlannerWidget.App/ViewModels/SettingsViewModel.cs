using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlannerWidget.App.Services;
using PlannerWidget.Core.Attendance;
using PlannerWidget.Core.Logging;
using PlannerWidget.Core.Settings;
using PlannerWidget.Core.Storage;

namespace PlannerWidget.App.ViewModels;

/// <summary>A Beállítások ablak nézetmodellje. Minden változás azonnal mentődik és érvénybe lép.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogService _dialogs;
    private readonly MainViewModel _main;
    private bool _loading;

    public SettingsViewModel(AppServices services, IDialogService dialogs, MainViewModel main)
    {
        _services = services;
        _dialogs = dialogs;
        _main = main;
        Load();
    }

    public static IReadOnlyList<string> ThemeOptions { get; } = ["Rendszer szerint", "Sötét", "Világos"];
    public static IReadOnlyList<string> HotkeyOptions { get; } = Hotkey.Presets;
    public static IReadOnlyList<string> CloseOptions { get; } = ["Kilép a programból", "Elrejti (a tálcán fut tovább)"];

    [ObservableProperty] public partial int ThemeIndex { get; set; }
    [ObservableProperty] public partial bool AlwaysOnTop { get; set; }
    [ObservableProperty] public partial double RefreshIntervalMinutes { get; set; }
    [ObservableProperty] public partial double ExpandedHeight { get; set; }
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

    public string VersionText { get; } =
        "Planner Widget " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "") + " · WinUI 3 / .NET 10";

    public string DataFolder => AppPaths.Root;

    public void Load()
    {
        _loading = true;
        var s = _services.Settings.Current;
        ThemeIndex = (int)s.Theme;
        AlwaysOnTop = s.AlwaysOnTop;
        RefreshIntervalMinutes = s.RefreshIntervalMinutes;
        ExpandedHeight = s.ExpandedHeight;
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
    partial void OnSoundsEnabledChanged(bool value) => Save(s => s.SoundsEnabled = value);
    partial void OnNotificationsEnabledChanged(bool value) => Save(s => s.NotificationsEnabled = value);
    partial void OnConfirmDeleteChanged(bool value) => Save(s => s.ConfirmDelete = value);
    partial void OnCloseButtonIndexChanged(int value) => Save(s => s.CloseButton = (CloseButtonBehavior)Math.Clamp(value, 0, 1));
    partial void OnUseWindowsAccountChanged(bool value) => Save(s => s.AuthMode = value ? AuthMode.Automatic : AuthMode.Browser);

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
