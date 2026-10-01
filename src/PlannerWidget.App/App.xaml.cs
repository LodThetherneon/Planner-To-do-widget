using System.Net;
using System.Reflection;
using Microsoft.UI.Xaml;
using PlannerWidget.App.Services;
using PlannerWidget.Core.Attendance;
using PlannerWidget.Core.Graph;
using PlannerWidget.Core.Logging;
using PlannerWidget.Core.Settings;
using PlannerWidget.Core.Storage;
using PlannerWidget.Core.Tasks;

namespace PlannerWidget.App;

/// <summary>Kompozíciós gyökér és életciklus: szolgáltatások létrehozása, tálca, gyorsbillentyű, kilépés.</summary>
public partial class App : Application
{
    private const int MenuShowHide = 1;
    private const int MenuRefresh = 2;
    private const int MenuNewTask = 3;
    private const int MenuWork = 4;
    private const int MenuSettings = 5;
    private const int MenuOpenPlanner = 6;
    private const int MenuQuit = 9;

    private AppServices? _services;
    private MainWindow? _window;
    private SettingsWindow? _settingsWindow;
    private ShellIntegration? _shell;
    private HttpClient? _http;
    private string? _registeredHotkey;
    private bool _shuttingDown;

    public App()
    {
        InitializeComponent();
        Log.Initialize(AppPaths.LogDirectory);
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Kezeletlen kivétel (AppDomain)", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Nem figyelt Task kivétel", e.Exception);
            e.SetObserved();
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log.Info($"Indítás – verzió {Assembly.GetExecutingAssembly().GetName().Version}, {Environment.OSVersion}");

        var demo = Program.IsDemo;
        var settings = new SettingsStore();

        _http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PlannerWidget/3.0");

        MainWindow? window = null;
        IAuthService auth = demo ? new DemoAuthService() : new AuthService(settings, () => window?.Handle ?? 0);
        IPlannerClient client = demo ? new DemoPlannerClient() : new PlannerClient(_http, auth);
        IPdfFormService pdf = demo ? new ReadOnlyPdfFormService(new PdfSharpFormService()) : new PdfSharpFormService();
        var planner = new PlannerService(client);

        if (demo)
        {
            Log.Info("Bemutató mód: kitalált adatok, a Planner és a PDF nem módosul.");
        }

        _services = new AppServices
        {
            Settings = settings,
            Auth = auth,
            IsDemo = demo,
            Planner = planner,
            Attendance = new AttendanceCoordinator(settings, new AttendanceService(pdf)),
            Sounds = new SoundService(settings),
            Notifications = new NotificationService(),
            Autostart = new AutostartService(),
        };

        window = _window = new MainWindow(_services);
        _window.QuitRequested += (_, _) => Shutdown();
        _window.SettingsRequested += (_, _) => ShowSettings();
        _window.Activate();

        SetupShell();
        _services.Notifications.Attach(_shell!);
        _services.Notifications.Activated += (_, _) => _window.ShowAndActivate(expand: true);
        settings.Changed += (_, _) => ApplySettings();
        Program.ActivatedByAnotherInstance += (_, _) => _window.DispatcherQueue.TryEnqueue(() => _window.ShowAndActivate(expand: true));

        try
        {
            await _window.ViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Indítási hiba", ex);
            _window.ViewModel.ShowError("Indítási hiba", ex.Message);
        }
    }

    private void SetupShell()
    {
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        _shell = new ShellIntegration(icon, "Planner Widget");
        _shell.TrayClicked += (_, _) => _window?.ToggleVisibility();
        _shell.HotkeyPressed += (_, _) => _window?.ToggleFromHotkey();
        _shell.MenuItemInvoked += OnTrayMenu;
        _shell.MenuProvider = () =>
        [
            new TrayMenuItem(MenuShowHide, _window?.AppWindow.IsVisible == true ? "Elrejtés" : "Megjelenítés"),
            new TrayMenuItem(MenuRefresh, "Frissítés", _window?.ViewModel.IsSignedIn == true),
            new TrayMenuItem(MenuNewTask, "Új feladat…", _window?.ViewModel.IsSignedIn == true),
            new TrayMenuItem(MenuWork, _window?.ViewModel.WorkButtonText ?? "Munka kezdete", _window?.ViewModel.IsSignedIn == true),
            TrayMenuItem.Separator,
            new TrayMenuItem(MenuOpenPlanner, "Megnyitás a Plannerben"),
            new TrayMenuItem(MenuSettings, "Beállítások…"),
            TrayMenuItem.Separator,
            new TrayMenuItem(MenuQuit, "Kilépés"),
        ];
        _shell.ShowTrayIcon();

        if (_window is not null)
        {
            _window.ViewModel.TrayTooltipChanged += (_, _) => _shell?.SetTooltip(_window.ViewModel.TrayTooltip);
        }

        ApplySettings();
    }

    private async void OnTrayMenu(object? sender, int id)
    {
        if (_window is null)
        {
            return;
        }

        switch (id)
        {
            case MenuShowHide:
                _window.ToggleVisibility();
                break;
            case MenuRefresh:
                await _window.ViewModel.RefreshAsync(userInitiated: true);
                break;
            case MenuNewTask:
                _window.ShowAndActivate(expand: true);
                await _window.ViewModel.OpenAddPanelAsync();
                break;
            case MenuWork:
                _window.ShowAndActivate(expand: true);
                await _window.ViewModel.ToggleWorkCommand.ExecuteAsync(null);
                break;
            case MenuOpenPlanner:
                _window.ViewModel.OpenPlannerCommand.Execute(null);
                break;
            case MenuSettings:
                ShowSettings();
                break;
            case MenuQuit:
                Shutdown();
                break;
        }
    }

    /// <summary>A beállítások azonnali érvényesítése (a Beállítások ablakból vagy máshonnan).</summary>
    private void ApplySettings()
    {
        if (_services is null || _window is null)
        {
            return;
        }

        var s = _services.Settings.Current;
        _window.ApplyTheme(s.Theme);
        _window.ApplyAlwaysOnTop(s.AlwaysOnTop);
        _window.ViewModel.ApplyRefreshInterval();

        if (_shell is not null && _registeredHotkey != s.Hotkey)
        {
            _registeredHotkey = s.Hotkey;
            var hotkey = Hotkey.Parse(s.Hotkey);
            var ok = _shell.RegisterHotkey(hotkey);
            _window.ViewModel.HotkeyHint = hotkey is null
                ? "Tipp: a widget a tálcaikonról is előhozható."
                : ok
                    ? $"Tipp: {hotkey.Display} bárhonnan előhozza a widgetet."
                    : $"A(z) {hotkey.Display} gyorsbillentyűt egy másik program használja – válassz másikat a beállításokban.";
            if (!ok)
            {
                _window.ViewModel.ShowStatus($"{hotkey!.Display} foglalt", ViewModels.StatusKind.Warning, TimeSpan.FromSeconds(5));
            }
        }
    }

    private void ShowSettings()
    {
        if (_services is null || _window is null)
        {
            return;
        }

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_services, _window.ViewModel);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        else
        {
            _settingsWindow.ViewModel.Load();
        }

        _settingsWindow.Activate();
        _settingsWindow.BringToFront();
    }

    private void Shutdown()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        Log.Info("Kilépés.");
        try
        {
            _settingsWindow?.Close();
            _shell?.Dispose();
            _http?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Hiba a leállítás közben", ex);
        }

        if (_window is not null)
        {
            _window.IsQuitting = true;
            _window.Close();
        }

        Exit();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Error("Kezeletlen UI kivétel", e.Exception);
        e.Handled = true;
        _window?.ViewModel.ShowError("Váratlan hiba", e.Message);
    }
}
