using PlannerWidget.Core.Attendance;
using PlannerWidget.Core.Tickets;

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

    /// <summary>Elmosott (akril) háttér, amikor a widget az aktív ablak (benne kattintgatsz).</summary>
    public bool BlurWhenActive { get; set; } = true;

    /// <summary>Elmosott háttér akkor is, ha máshová kattintasz (alapból egyszínűre vált, mint a Windows többi ablaka).</summary>
    public bool BlurWhenInactive { get; set; }

    // ---- Ablak állapota ---------------------------------------------------------------------
    /// <summary>Az ablak jobb széle és teteje fizikai pixelben (a widget a jobb széléhez igazodik).</summary>
    public int? WindowRight { get; set; }
    public int? WindowTop { get; set; }
    public bool Expanded { get; set; } = true;
    public bool Compact { get; set; }
    public double ExpandedHeight { get; set; } = 640;

    /// <summary>A widget szélessége (DIP, 100%-os nagyításnál); a kompakt nézet ettől független.</summary>
    public double WindowWidth { get; set; } = DefaultWindowWidth;

    public const double DefaultWindowWidth = 380;
    public const double MinWindowWidth = 320;
    public const double MaxWindowWidth = 1000;

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

    // ---- Beérkező ügyek ---------------------------------------------------------------------
    public TicketSettings Tickets { get; set; } = new();

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

/// <summary>
/// „Beérkező ügyek”: a SharePoint-ticketeket Power Automate flow-k teszik ki Planner-feladatként egy külön tervbe.
/// Ügyterv nélkül (PlanId == null) nincs ügyfelismerés.
/// </summary>
public sealed class TicketSettings
{
    /// <summary>Az „Ügyek” terv (csoport: MFÜI - RFK) – ez az alapértelmezett ügyterv.</summary>
    public const string DefaultPlanId = "LWXZVkNKt0GrJU8T6PDftpYAEcV0";

    public const string DefaultPlanTitle = "Ügyek";

    /// <summary>
    /// Az ügyterv. Ha hiányzik (új telepítés, régi settings.json), a <c>SettingsStore</c> az alapértelmezett tervet
    /// állítja be – kivéve, ha a felhasználó tudatosan a „Nincs”-et választotta (<see cref="NoPlan"/>).
    /// Szándékosan nincs mezőszintű alapértéke: a null nem kerül a JSON-ba, így betöltéskor a „Nincs” felülíródna.
    /// </summary>
    public string? PlanId { get; set; }

    /// <summary>A felhasználó kifejezetten a „Nincs” ügytervet választotta: ilyenkor nem állítjuk vissza az alapértéket.</summary>
    public bool NoPlan { get; set; }
    public string UrlTemplate { get; set; } = TicketLinks.DefaultUrlTemplate;
    public bool NotifyNewTickets { get; set; } = true;
    public bool ConfirmCloseTicket { get; set; } = true;
}
