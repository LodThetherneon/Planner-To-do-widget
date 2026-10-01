using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PlannerWidget.App.Interop;
using PlannerWidget.App.Services;
using PlannerWidget.App.ViewModels;
using PlannerWidget.Core.Settings;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace PlannerWidget.App;

/// <summary>
/// A widget ablaka: keret és címsor nélküli, akril hátterű, a képernyő jobb széléhez igazodó.
/// Három méretállapota van: lenyitva, csak fejléc, és oldalra csukva (kompakt).
/// Méretváltáskor a jobb széle helyben marad, és röviden animál.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const double WidthDip = 380;
    private const double HeaderDip = 48;
    private const double MinCompactWidthDip = 150;
    private const int ScreenMarginDip = 12;
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(180);

    private readonly AppServices _services;
    private readonly OverlappedPresenter _presenter;
    private readonly DispatcherQueueTimer _animationTimer;
    private readonly DispatcherQueueTimer _savePositionTimer;
    private readonly DispatcherQueueTimer _topmostResetTimer;
    private nint _hwnd;
    private bool _expanded;
    private bool _compact;
    private bool _animating;
    private bool _layoutPending;
    private RectInt32 _animationFrom;
    private RectInt32 _animationTo;
    private DateTime _animationStart;
    private SizeInt32? _borderDelta;
    private double _lastScale;

    public MainWindow(AppServices services)
    {
        _services = services;
        Dialogs = new DialogService(() => Content?.XamlRoot, () => _hwnd, () => RootGrid.ActualTheme, EnsureExpandedForDialogAsync);
        ViewModel = new MainViewModel(services, Dialogs, DispatcherQueue);

        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        _presenter = AppWindow.Presenter as OverlappedPresenter ?? OverlappedPresenter.Create();
        _animationTimer = DispatcherQueue.CreateTimer();
        _animationTimer.Interval = TimeSpan.FromMilliseconds(15);
        _animationTimer.Tick += OnAnimationTick;
        _savePositionTimer = DispatcherQueue.CreateTimer();
        _savePositionTimer.Interval = TimeSpan.FromMilliseconds(600);
        _savePositionTimer.IsRepeating = false;
        _savePositionTimer.Tick += (_, _) => SavePosition();
        _topmostResetTimer = DispatcherQueue.CreateTimer();
        _topmostResetTimer.Interval = TimeSpan.FromMilliseconds(400);
        _topmostResetTimer.IsRepeating = false;
        _topmostResetTimer.Tick += (_, _) => _presenter.IsAlwaysOnTop = _services.Settings.Current.AlwaysOnTop;

        _expanded = services.Settings.Current.Expanded;
        _compact = services.Settings.Current.Compact;

        ConfigureWindow();
        ApplyLayout(animate: false, initial: true);

        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += OnAppWindowClosing;
        DragRegion.SizeChanged += (_, _) => UpdateDragRegion();
        RootGrid.Loaded += OnRootLoaded;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.ScrollToTopRequested += (_, _) => DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low,
            () => TaskScroller.ChangeView(null, 0, null, disableAnimation: true));
        ViewModel.AddPanelOpened += (_, _) => DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            EnsureExpanded();
            NewTaskTitleBox.Focus(FocusState.Programmatic);
        });
    }

    public MainViewModel ViewModel { get; }

    public DialogService Dialogs { get; }

    public nint Handle => _hwnd;

    public event EventHandler? SettingsRequested;

    public event EventHandler? QuitRequested;

    /// <summary>Igaz, ha az alkalmazás tényleg kilép (ilyenkor a bezárást nem fogjuk meg).</summary>
    public bool IsQuitting { get; set; }

    // ---- Ablak beállítása ------------------------------------------------------------------

    private void ConfigureWindow()
    {
        AppWindow.Title = "Planner Widget";
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        // Ne jelenjen meg a tálcán és az Alt+Tab listában – ez egy widget.
        AppWindow.IsShownInSwitchers = false;

        _presenter.IsResizable = false;
        _presenter.IsMaximizable = false;
        _presenter.IsMinimizable = false;
        _presenter.SetBorderAndTitleBar(true, false);
        _presenter.IsAlwaysOnTop = _services.Settings.Current.AlwaysOnTop;
        AppWindow.SetPresenter(_presenter);

        SystemBackdrop = new DesktopAcrylicBackdrop();
        ApplyTheme(_services.Settings.Current.Theme);
    }

    public void ApplyTheme(AppTheme theme) => RootGrid.RequestedTheme = Ui.ToElementTheme(theme);

    public void ApplyAlwaysOnTop(bool value) => _presenter.IsAlwaysOnTop = value;

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        _lastScale = RootGrid.XamlRoot.RasterizationScale;
        RootGrid.XamlRoot.Changed += (_, _) =>
        {
            var scale = RootGrid.XamlRoot.RasterizationScale;
            if (Math.Abs(scale - _lastScale) > 0.001)
            {
                // Másik DPI-jű monitorra került: a méretet újraszámoljuk.
                _lastScale = scale;
                _borderDelta = null;
                ApplyLayout(animate: false);
            }
        };
        UpdateDragRegion();
    }

    // ---- Méretállapotok --------------------------------------------------------------------

    private void OnExpandClick(object sender, RoutedEventArgs e) => SetExpanded(!_expanded);

    private void OnCompactClick(object sender, RoutedEventArgs e) => SetCompact(!_compact);

    public void SetExpanded(bool expanded, bool animate = true)
    {
        _expanded = expanded;
        if (expanded)
        {
            _compact = false;
        }

        ApplyLayout(animate);
        PersistLayoutState();
    }

    public void SetCompact(bool compact)
    {
        _compact = compact;
        ApplyLayout(animate: true);
        PersistLayoutState();
    }

    public void EnsureExpanded()
    {
        if (!_expanded || _compact)
        {
            SetExpanded(true, animate: false);
        }
    }

    private async Task EnsureExpandedForDialogAsync()
    {
        if (!AppWindow.IsVisible)
        {
            AppWindow.Show(true);
        }

        if (!_expanded || _compact)
        {
            SetExpanded(true, animate: false);
            await Task.Delay(60); // Hagyjuk lefutni az elrendezést, mielőtt a párbeszéd megjelenik.
        }
    }

    private void PersistLayoutState()
    {
        var settings = _services.Settings.Current;
        if (settings.Expanded != _expanded || settings.Compact != _compact)
        {
            _services.Settings.Update(s =>
            {
                s.Expanded = _expanded;
                s.Compact = _compact;
            });
        }
    }

    private void ApplyLayout(bool animate, bool initial = false)
    {
        var showContent = _expanded && !_compact;
        if (showContent)
        {
            ContentRoot.Visibility = Visibility.Visible;
        }
        else
        {
            ContentRoot.Visibility = Visibility.Collapsed;
        }

        FullHeaderButtons.Visibility = _compact ? Visibility.Collapsed : Visibility.Visible;
        UpdateHeaderPills();
        CompactButton.Content = Ui.CompactGlyph(_compact);
        ExpandButton.Content = Ui.ExpandGlyph(_expanded);
        ToolTipService.SetToolTip(CompactButton, _compact ? "Kinyitás" : "Összecsukás oldalra");

        var outer = ToOuter(TargetClientSize());
        RectInt32 target;
        if (initial)
        {
            target = InitialRect(outer);
        }
        else
        {
            var position = AppWindow.Position;
            var size = AppWindow.Size;
            var right = (_animating ? _animationTo.X + _animationTo.Width : position.X + size.Width);
            target = new RectInt32(right - outer.Width, _animating ? _animationTo.Y : position.Y, outer.Width, outer.Height);
        }

        target = ClampToWorkArea(target);

        if (!animate || initial)
        {
            _animating = false;
            _animationTimer.Stop();
            AppWindow.MoveAndResize(target);
            OnLayoutApplied();
            return;
        }

        _animationFrom = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        _animationTo = target;
        _animationStart = DateTime.UtcNow;
        _animating = true;
        _animationTimer.Start();
    }

    private void OnAnimationTick(DispatcherQueueTimer sender, object args)
    {
        var t = Math.Clamp((DateTime.UtcNow - _animationStart) / AnimationDuration, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3); // ease-out cubic
        int Lerp(int a, int b) => (int)Math.Round(a + (b - a) * eased);

        var width = Lerp(_animationFrom.Width, _animationTo.Width);
        var height = Lerp(_animationFrom.Height, _animationTo.Height);
        var right = _animationTo.X + _animationTo.Width;
        AppWindow.MoveAndResize(new RectInt32(right - width, _animationTo.Y, width, height));

        if (t >= 1)
        {
            _animationTimer.Stop();
            _animating = false;
            AppWindow.MoveAndResize(_animationTo);
            OnLayoutApplied();
        }
    }

    private void OnLayoutApplied()
    {
        UpdateDragRegion();
        SavePosition();
    }

    private SizeInt32 TargetClientSize()
    {
        var scale = Scale;
        var widthDip = _compact ? MeasureCompactWidth() : WidthDip;
        var heightDip = _expanded && !_compact ? _services.Settings.Current.ExpandedHeight : HeaderDip;

        var work = WorkArea();
        var maxHeight = work.Height - 2 * (int)(ScreenMarginDip * scale);
        var height = Math.Min((int)Math.Round(heightDip * scale), Math.Max(maxHeight, (int)(HeaderDip * scale)));
        return new SizeInt32((int)Math.Round(widthDip * scale), height);
    }

    private double MeasureCompactWidth()
    {
        HeaderContent.Measure(new Size(double.PositiveInfinity, HeaderDip));
        var content = HeaderContent.DesiredSize.Width;
        // bal margó + tartalom + távolság + kompakt gomb + jobb margó
        return Math.Max(MinCompactWidthDip, 14 + content + 10 + 32 + 8);
    }

    private SizeInt32 ToOuter(SizeInt32 client)
    {
        _borderDelta ??= new SizeInt32(
            Math.Max(0, AppWindow.Size.Width - AppWindow.ClientSize.Width),
            Math.Max(0, AppWindow.Size.Height - AppWindow.ClientSize.Height));
        return new SizeInt32(client.Width + _borderDelta.Value.Width, client.Height + _borderDelta.Value.Height);
    }

    private double Scale
    {
        get
        {
            var dpi = NativeMethods.GetDpiForWindow(_hwnd);
            return dpi == 0 ? 1.0 : dpi / 96.0;
        }
    }

    private RectInt32 WorkArea() => DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;

    private RectInt32 InitialRect(SizeInt32 outer)
    {
        var settings = _services.Settings.Current;
        if (settings.WindowRight is { } right && settings.WindowTop is { } top &&
            DisplayArea.GetFromPoint(new PointInt32(right - 20, top + 20), DisplayAreaFallback.None) is not null)
        {
            return new RectInt32(right - outer.Width, top, outer.Width, outer.Height);
        }

        var work = DisplayArea.Primary.WorkArea;
        var margin = (int)(ScreenMarginDip * Scale);
        return new RectInt32(work.X + work.Width - outer.Width - margin, work.Y + margin, outer.Width, outer.Height);
    }

    private RectInt32 ClampToWorkArea(RectInt32 rect)
    {
        var area = DisplayArea.GetFromRect(rect, DisplayAreaFallback.Nearest).WorkArea;
        var x = Math.Clamp(rect.X, area.X, Math.Max(area.X, area.X + area.Width - rect.Width));
        var y = Math.Clamp(rect.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - rect.Height));
        return new RectInt32(x, y, rect.Width, rect.Height);
    }

    /// <summary>A fejléc szöveges része húzható (mint egy címsor); a gombok kattinthatók maradnak.</summary>
    private void UpdateDragRegion()
    {
        if (Content?.XamlRoot is not { } root || DragRegion.ActualWidth <= 0)
        {
            return;
        }

        var scale = root.RasterizationScale;
        var bounds = DragRegion.TransformToVisual(null).TransformBounds(new Rect(0, 0, DragRegion.ActualWidth, DragRegion.ActualHeight));
        var rect = new RectInt32(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            (int)Math.Round(bounds.Width * scale),
            (int)Math.Round(bounds.Height * scale));
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Caption, [rect]);
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPositionChange && !_animating)
        {
            _savePositionTimer.Stop();
            _savePositionTimer.Start();
        }
    }

    private void SavePosition()
    {
        if (!AppWindow.IsVisible)
        {
            return;
        }

        var right = AppWindow.Position.X + AppWindow.Size.Width;
        var top = AppWindow.Position.Y;
        var settings = _services.Settings.Current;
        if (settings.WindowRight != right || settings.WindowTop != top)
        {
            _services.Settings.Update(s =>
            {
                s.WindowRight = right;
                s.WindowTop = top;
            });
        }
    }

    /// <summary>
    /// Lenyitva a munkaidő az eszköztár gombján látszik, ezért a fejlécben csak összecsukva jelenik meg.
    /// Összecsukva (teljes szélesség, 4 gomb) a „mára” címke helyet ad neki, hogy ne lógjon a gombok alá.
    /// </summary>
    private void UpdateHeaderPills()
    {
        var contentVisible = _expanded && !_compact;
        var showWork = ViewModel.IsWorkRunning && !contentVisible;
        WorkPill.Visibility = showWork ? Visibility.Visible : Visibility.Collapsed;
        TodayPill.Visibility = ViewModel.HasDueToday && !(showWork && !_compact) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsWorkRunning) or nameof(MainViewModel.HasDueToday) or nameof(MainViewModel.DueTodayCount))
        {
            UpdateHeaderPills();
        }

        if (!_compact || _layoutPending)
        {
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.HeaderTitle) or nameof(MainViewModel.HasOverdue) or nameof(MainViewModel.HasDueToday)
            or nameof(MainViewModel.HasStatus) or nameof(MainViewModel.StatusMessage) or nameof(MainViewModel.IsWorkRunning)
            or nameof(MainViewModel.WorkElapsedText))
        {
            // Kompakt módban a szélesség a fejléc szövegéhez igazodik.
            _layoutPending = true;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                _layoutPending = false;
                ApplyLayout(animate: false);
            });
        }
    }

    // ---- Megjelenítés / elrejtés -----------------------------------------------------------

    public bool IsForeground => NativeMethods.GetForegroundWindow() == _hwnd;

    /// <summary>Előtérbe hozás (gyorsbillentyű, tálca, értesítés).</summary>
    public void ShowAndActivate(bool expand)
    {
        if (!AppWindow.IsVisible)
        {
            AppWindow.Show(true);
        }

        if (expand && (!_expanded || _compact))
        {
            SetExpanded(true);
        }

        // Rövid időre „mindig felül”, hogy biztosan a többi ablak fölé kerüljön.
        _presenter.IsAlwaysOnTop = true;
        NativeMethods.SetForegroundWindow(_hwnd);
        Activate();
        _topmostResetTimer.Stop();
        _topmostResetTimer.Start();

        ViewModel.RefreshIfStale();
    }

    /// <summary>Gyorsbillentyű: ha elöl van, elrejti; különben előhozza.</summary>
    public void ToggleFromHotkey()
    {
        if (AppWindow.IsVisible && IsForeground && _expanded && !_compact)
        {
            AppWindow.Hide();
        }
        else
        {
            ShowAndActivate(expand: true);
        }
    }

    public void ToggleVisibility()
    {
        if (AppWindow.IsVisible)
        {
            AppWindow.Hide();
        }
        else
        {
            ShowAndActivate(expand: false);
        }
    }

    public void HideToTray() => AppWindow.Hide();

    private void OnCloseClick(object sender, RoutedEventArgs e) => HandleCloseRequest();

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (IsQuitting)
        {
            return;
        }

        args.Cancel = true;
        HandleCloseRequest();
    }

    private void HandleCloseRequest()
    {
        if (_services.Settings.Current.CloseButton == CloseButtonBehavior.HideToTray)
        {
            AppWindow.Hide();
        }
        else
        {
            QuitRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnQuitClick(object sender, RoutedEventArgs e) => QuitRequested?.Invoke(this, EventArgs.Empty);

    // ---- Billentyűzet ----------------------------------------------------------------------

    private void OnRefreshAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ViewModel.RefreshCommand.Execute(null);
    }

    private async void OnNewTaskAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ViewModel.IsSignedIn)
        {
            EnsureExpanded();
            await ViewModel.OpenAddPanelAsync();
        }
    }

    private void OnSearchAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ViewModel.IsSignedIn)
        {
            EnsureExpanded();
            SearchBox.Focus(FocusState.Keyboard);
        }
    }

    private void OnEscapeAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ViewModel.IsAddPanelOpen)
        {
            ViewModel.IsAddPanelOpen = false;
        }
        else if (!string.IsNullOrEmpty(ViewModel.SearchText))
        {
            ViewModel.SearchText = "";
        }
        else if (_expanded)
        {
            SetExpanded(false);
        }
    }

    private void OnNewTaskTitleKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ViewModel.AddTaskCommand.CanExecute(null))
        {
            e.Handled = true;
            ViewModel.AddTaskCommand.Execute(null);
        }
    }
}
