using PlannerWidget.Core.Storage;

namespace PlannerWidget.Core.Settings;

/// <summary>A beállítások egyetlen példánya + mentés. Minden módosítás után hívd a <see cref="Save"/>-et.</summary>
public sealed class SettingsStore
{
    private readonly JsonFileStore<AppSettings> _store;

    public SettingsStore(string? path = null)
    {
        _store = new JsonFileStore<AppSettings>(path ?? AppPaths.SettingsFile);
        Current = _store.Load() ?? new AppSettings();
        Normalize(Current);
    }

    public AppSettings Current { get; }

    public event EventHandler? Changed;

    public void Save()
    {
        Normalize(Current);
        _store.Save(Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Update(Action<AppSettings> change)
    {
        change(Current);
        Save();
    }

    private static void Normalize(AppSettings s)
    {
        if (s.SchemaVersion < 2)
        {
            // 1 → 2: az alapértelmezett bejelentkezés böngészős lett (a WAM külön regisztrációt igényel).
            s.AuthMode = AuthMode.Browser;
        }

        s.SchemaVersion = AppSettings.CurrentSchemaVersion;
        s.RefreshIntervalMinutes = Math.Clamp(s.RefreshIntervalMinutes, 1, 120);
        s.ExpandedHeight = Math.Clamp(s.ExpandedHeight, 320, 1400);
        s.DefaultBucketByPlan ??= new();
        s.Attendance ??= new();
        if (string.IsNullOrWhiteSpace(s.Hotkey))
        {
            s.Hotkey = "Alt+W";
        }
    }
}
