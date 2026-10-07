using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PlannerWidget.App.ViewModels;
using PlannerWidget.Core.Models;
using PlannerWidget.Core.Tasks;
using Windows.UI;
using Windows.UI.Text;

namespace PlannerWidget.App;

/// <summary>
/// x:Bind segédfüggvények (konverterek helyett). A színek világos és sötét témán is olvashatók.
/// </summary>
public static class Ui
{
    private static readonly Dictionary<uint, SolidColorBrush> Cache = new();

    public static readonly Color Red = Color.FromArgb(255, 0xE5, 0x48, 0x4D);
    public static readonly Color Amber = Color.FromArgb(255, 0xD2, 0x99, 0x22);
    public static readonly Color Green = Color.FromArgb(255, 0x30, 0xA4, 0x6C);
    public static readonly Color Blue = Color.FromArgb(255, 0x3B, 0x82, 0xF6);
    public static readonly Color Orange = Color.FromArgb(255, 0xF0, 0x88, 0x3E);
    public static readonly Color Gray = Color.FromArgb(255, 0x8B, 0x94, 0x9E);
    public static readonly Color Purple = Color.FromArgb(255, 0x8B, 0x5C, 0xF6);
    public static readonly Color Yellow = Color.FromArgb(255, 0xE3, 0xB3, 0x41);

    /// <summary>Szinkronhiba-jelzés: hiba esetén piros, csak leállt életjelnél narancs.</summary>
    public static SolidColorBrush SyncBrush(bool isError) => isError ? Brush(Red) : Brush(Orange);

    public static SolidColorBrush SyncWarningBrush => Brush(Orange);

    /// <summary>Az ügyek lilája (cím, „#123”, karika) – sötét és világos témán is olvasható.</summary>
    public static readonly Color TicketPurple = Color.FromArgb(255, 0x9B, 0x7B, 0xF4);

    public static SolidColorBrush Brush(Color color, byte alpha = 255)
    {
        var key = ((uint)alpha << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        if (!Cache.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            Cache[key] = brush;
        }

        return brush;
    }

    public static SolidColorBrush RedBrush => Brush(Red);
    public static SolidColorBrush RedSoftBrush => Brush(Red, 40);
    public static SolidColorBrush AmberBrush => Brush(Amber);
    public static SolidColorBrush AmberSoftBrush => Brush(Amber, 40);
    public static SolidColorBrush BlueBrush => Brush(Blue);
    public static SolidColorBrush BlueSoftBrush => Brush(Blue, 40);
    public static SolidColorBrush PurpleBrush => Brush(Purple);
    public static SolidColorBrush PurpleSoftBrush => Brush(Purple, 40);

    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility VisibleIfText(string? value) => string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;

    public static bool Not(bool value) => !value;

    /// <summary>A kártya bal oldali színsávja – a régi verzió piros/sárga/zöld logikája.</summary>
    public static SolidColorBrush Accent(DueState state) => state switch
    {
        DueState.Overdue => Brush(Red),
        DueState.DueToday or DueState.DueSoon => Brush(Amber),
        DueState.Later => Brush(Green),
        DueState.NoDueDate => Brush(Gray, 160),
        _ => Brush(Gray, 90),
    };

    public static SolidColorBrush DueForeground(DueState state) => state switch
    {
        DueState.Overdue => Brush(Red),
        DueState.DueToday or DueState.DueSoon => Brush(Amber),
        _ => Brush(Gray),
    };

    public static SolidColorBrush DueBackground(DueState state) => state switch
    {
        DueState.Overdue => Brush(Red, 40),
        DueState.DueToday => Brush(Amber, 40),
        _ => Brush(Colors.Transparent, 0),
    };

    public static SolidColorBrush TicketBrush => Brush(TicketPurple);

    /// <summary>Az ügy-csempe halvány lila árnyalata (sima feladatnál átlátszó).</summary>
    public static SolidColorBrush TicketCardTint(bool isTicket) => isTicket ? Brush(TicketPurple, 26) : Brush(Colors.Transparent, 0);

    /// <summary>Az ügy státuszcímkéjének színe (a gyűjtő neve szerint).</summary>
    public static SolidColorBrush TicketStatusBrush(string? status) => Brush(TicketStatusColor(status));

    public static SolidColorBrush TicketStatusSoftBrush(string? status) => Brush(TicketStatusColor(status), 40);

    /// <summary>New szürke, Processed kék, In progress narancs, SOS piros, KÉRDÉSES lila, Revisit sárga (Done zöld).</summary>
    private static Color TicketStatusColor(string? status) => status switch
    {
        Core.Tickets.TicketStatuses.New => Gray,
        Core.Tickets.TicketStatuses.Processed => Blue,
        Core.Tickets.TicketStatuses.InProgress => Orange,
        Core.Tickets.TicketStatuses.Sos => Red,
        Core.Tickets.TicketStatuses.Questionable => Purple,
        Core.Tickets.TicketStatuses.Revisit => Yellow,
        Core.Tickets.TicketStatuses.Done => Green,
        _ => Gray,
    };

    /// <summary>Karakterszámláló: szürke, a korlát 90%-ától sárga, a korlátnál piros.</summary>
    public static SolidColorBrush CounterBrush(int length, int max) =>
        length >= max ? Brush(Red) : length >= max * 0.9 ? Brush(Amber) : Brush(Gray);

    public static SolidColorBrush PriorityBrush(TaskPriority priority) => priority switch
    {
        TaskPriority.Urgent => Brush(Red),
        TaskPriority.Important => Brush(Orange),
        TaskPriority.Medium => Brush(Gray),
        _ => Brush(Blue),
    };

    /// <summary>A „Közepes” az alapértelmezett, azt nem jelezzük külön (kevesebb vizuális zaj).</summary>
    public static Visibility PriorityVisibility(TaskPriority priority, bool completed) =>
        Visible(!completed && priority != TaskPriority.Medium);

    /// <summary>Kész: teli zöld pipás kör; aktív: üres gyűrű.</summary>
    public static string CheckGlyph(bool completed) => completed ? "" : "";

    public static SolidColorBrush CheckBrush(bool completed) => completed ? Brush(Green) : Brush(Gray);

    public static double TitleOpacity(bool completed) => completed ? 0.6 : 1.0;

    public static TextDecorations Strike(bool completed) => completed ? TextDecorations.Strikethrough : TextDecorations.None;

    public static string ExpandGlyph(bool expanded) => expanded ? "" : "";

    public static string CompactGlyph(bool compact) => compact ? "" : "";

    public static SolidColorBrush StatusBrush(StatusKind kind) => kind switch
    {
        StatusKind.Success => Brush(Green),
        StatusKind.Warning => Brush(Amber),
        StatusKind.Error => Brush(Red),
        _ => Brush(Gray),
    };

    public static InfoBarSeverity Severity(StatusKind kind) => kind switch
    {
        StatusKind.Success => InfoBarSeverity.Success,
        StatusKind.Warning => InfoBarSeverity.Warning,
        StatusKind.Error => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    };

    public static ElementTheme ToElementTheme(Core.Settings.AppTheme theme) => theme switch
    {
        Core.Settings.AppTheme.Dark => ElementTheme.Dark,
        Core.Settings.AppTheme.Light => ElementTheme.Light,
        _ => ElementTheme.Default,
    };
}
