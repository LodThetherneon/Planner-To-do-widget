using PlannerWidget.Core.Settings;
using PlannerWidget.Core.Tasks;

namespace PlannerWidget.App.Services;

/// <summary>Az alkalmazás szolgáltatásai egy helyen (kompozíciós gyökér: <see cref="App"/>).</summary>
public sealed class AppServices
{
    public required SettingsStore Settings { get; init; }
    public required IAuthService Auth { get; init; }
    public bool IsDemo { get; init; }
    public required PlannerService Planner { get; init; }
    public required AttendanceCoordinator Attendance { get; init; }
    public required SoundService Sounds { get; init; }
    public required NotificationService Notifications { get; init; }
    public required AutostartService Autostart { get; init; }
}
