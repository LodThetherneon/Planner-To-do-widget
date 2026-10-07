using PlannerWidget.Core.Settings;
using PlannerWidget.Core.Sync;
using PlannerWidget.Core.Tasks;
using PlannerWidget.Core.Tickets;
using PlannerWidget.Core.Wrapped;

namespace PlannerWidget.App.Services;

/// <summary>Az alkalmazás szolgáltatásai egy helyen (kompozíciós gyökér: <see cref="App"/>).</summary>
public sealed class AppServices
{
    public required SettingsStore Settings { get; init; }
    public required IAuthService Auth { get; init; }
    public bool IsDemo { get; init; }
    public required PlannerService Planner { get; init; }
    public required TicketTracker Tickets { get; init; }
    public required WrappedService Wrapped { get; init; }
    public required ManualOrderStore TaskOrder { get; init; }
    public required SyncErrorService SyncErrors { get; init; }
    public required AttendanceCoordinator Attendance { get; init; }
    public required SoundService Sounds { get; init; }
    public required NotificationService Notifications { get; init; }
    public required AutostartService Autostart { get; init; }
}
