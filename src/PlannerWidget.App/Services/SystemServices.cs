using Microsoft.Win32;
using PlannerWidget.App.Interop;
using PlannerWidget.Core.Logging;
using PlannerWidget.Core.Settings;

namespace PlannerWidget.App.Services;

public enum SoundEffect
{
    Start,
    Complete,
    Reopen,
}

/// <summary>Rövid WAV effektek (a régi verzió hangjai), aszinkron lejátszással.</summary>
public sealed class SoundService(SettingsStore settings)
{
    public void Play(SoundEffect effect)
    {
        if (!settings.Current.SoundsEnabled)
        {
            return;
        }

        var file = effect switch
        {
            SoundEffect.Start => "start.wav",
            SoundEffect.Complete => "complete.wav",
            _ => "reopen.wav",
        };
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", file);
        if (File.Exists(path))
        {
            NativeMethods.PlaySound(path, 0, NativeMethods.SND_FILENAME | NativeMethods.SND_ASYNC | NativeMethods.SND_NODEFAULT);
        }
    }
}

/// <summary>
/// Windows értesítések a tálcaikonon keresztül (Windows 10/11-en toastként jelennek meg).
/// Szándékosan nem a Windows App SDK AppNotification API-ja: az önálló (self-contained)
/// telepítésben nem érhető el, ez viszont minden környezetben működik.
/// </summary>
public sealed class NotificationService
{
    private ShellIntegration? _shell;

    public event EventHandler? Activated;

    public void Attach(ShellIntegration shell)
    {
        _shell = shell;
        shell.NotificationClicked += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
    }

    public void Show(string title, string body, IEnumerable<string>? lines = null)
    {
        try
        {
            var extra = lines?.Take(3).ToList();
            var text = extra is { Count: > 0 } ? body + "\n" + string.Join("\n", extra) : body;
            _shell?.ShowNotification(title, text);
        }
        catch (Exception ex)
        {
            Log.Warn("Értesítés megjelenítése nem sikerült", ex);
        }
    }
}

/// <summary>Indítás a Windows-zal (HKCU\...\Run), rendszergazdai jog nélkül.</summary>
public sealed class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PlannerWidget";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Contains(ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PlannerWidget.exe");

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{ExecutablePath}\" --autostart");
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName);
        }
    }
}
