using PlannerWidget.Core.Attendance;

namespace PlannerWidget.Core.Settings;

public enum AppTheme
{
    System,
    Dark,
    Light,
}

public enum CloseButtonBehavior
{
    /// <summary>Az X gomb kilép a programból (a régi verzió viselkedése).</summary>
    Quit,

    /// <summary>Az X gomb csak elrejti az ablakot, a program a tálcán fut tovább.</summary>
    HideToTray,
}

public enum AuthMode
{
    /// <summary>Windows-fiók (WAM) egyszeri bejelentkezéssel, ha az alkalmazásregisztráció engedi; különben böngésző.</summary>
    Automatic,

    /// <summary>Mindig a rendszer böngészőjében jelentkezik be.</summary>
    Browser,
}

/// <summary>A felhasználó beállításai és a megőrzendő állapot (settings.json).</summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    // ---- Megjelenés --------------------------------------------------------------------------
    public AppTheme Theme { get; set; } = AppTheme.System;
    public bool AlwaysOnTop { get; set; }

    // ---- Ablak állapota ---------------------------------------------------------------------
    /// <summary>Az ablak jobb széle és teteje fizikai pixelben (a widget a jobb széléhez igazodik).</summary>
    public int? WindowRight { get; set; }
    public int? WindowTop { get; set; }
    public bool Expanded { get; set; } = true;
    public bool Compact { get; set; }
    public double ExpandedHeight { get; set; } = 640;

    // ---- Viselkedés -------------------------------------------------------------------------
    public int RefreshIntervalMinutes { get; set; } = 5;
    public bool SoundsEnabled { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public bool ConfirmDelete { get; set; } = true;
    public string Hotkey { get; set; } = "Alt+W";
    public CloseButtonBehavior CloseButton { get; set; } = CloseButtonBehavior.Quit;

    /// <summary>
    /// Alapból böngészős bejelentkezés: ez a régi verzió alkalmazásregisztrációjával biztosan működik.
    /// A Windows-fiókos (WAM) mód a beállításokban kapcsolható be (README: plusz átirányítási cím kell hozzá).
    /// </summary>
    public AuthMode AuthMode { get; set; } = AuthMode.Browser;

    // ---- Planner ----------------------------------------------------------------------------
    /// <summary>Tervenként az alapértelmezett bucket (oszlop) új feladathoz.</summary>
    public Dictionary<string, string> DefaultBucketByPlan { get; set; } = new();
    public string? LastPlanId { get; set; }

    // ---- Jelenléti ív -----------------------------------------------------------------------
    public AttendanceSettings Attendance { get; set; } = new();

    // ---- Belső állapot ----------------------------------------------------------------------
    public DateOnly? LastDailyNotification { get; set; }
}

public sealed class AttendanceSettings
{
    public string? PdfPath { get; set; }
    public AttendanceTemplates? Templates { get; set; }
    public bool WorkRunning { get; set; }
    public DateTime? WorkStart { get; set; }
}
