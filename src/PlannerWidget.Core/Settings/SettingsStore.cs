using PlannerWidget.Core.Storage;
using PlannerWidget.Core.Tickets;

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
        s.WindowWidth = s.WindowWidth <= 0 || double.IsNaN(s.WindowWidth)
            ? AppSettings.DefaultWindowWidth
            : Math.Clamp(s.WindowWidth, AppSettings.MinWindowWidth, AppSettings.MaxWindowWidth);
        s.DefaultBucketByPlan ??= new();
        s.Attendance ??= new();
        s.Tickets ??= new();
        if (string.IsNullOrWhiteSpace(s.Tickets.UrlTemplate))
        {
            s.Tickets.UrlTemplate = TicketLinks.DefaultUrlTemplate;
        }

        if (string.IsNullOrWhiteSpace(s.Tickets.PlanId))
        {
            // Hiányzó érték ≠ tudatos „Nincs”: az előbbinél az alapértelmezett ügyterv jön.
            s.Tickets.PlanId = s.Tickets.NoPlan ? null : TicketSettings.DefaultPlanId;
        }
        else
        {
            s.Tickets.NoPlan = false;
        }
        if (string.IsNullOrWhiteSpace(s.Hotkey))
        {
            s.Hotkey = "Alt+W";
        }
    }
}
