using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PlannerWidget.App.Interop;
using PlannerWidget.App.Services;
using PlannerWidget.App.ViewModels;
using Windows.Graphics;
using WinRT.Interop;

namespace PlannerWidget.App;

public sealed partial class SettingsWindow : Window
{
    private readonly nint _hwnd;

    public SettingsWindow(AppServices services, MainViewModel main)
    {
        var dialogs = new DialogService(() => Content?.XamlRoot, () => _hwnd, () => RootGrid.ActualTheme);
        ViewModel = new SettingsViewModel(services, dialogs, main);
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        SystemBackdrop = new MicaBackdrop();
        RootGrid.RequestedTheme = Ui.ToElementTheme(services.Settings.Current.Theme);
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        var scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        if (scale <= 0)
        {
            scale = 1;
        }

        var size = new SizeInt32((int)(560 * scale), (int)(780 * scale));
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        size.Height = Math.Min(size.Height, work.Height - 40);
        AppWindow.MoveAndResize(new RectInt32(
            work.X + (work.Width - size.Width) / 2,
            work.Y + (work.Height - size.Height) / 2,
            size.Width,
            size.Height));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
        }

        services.Settings.Changed += OnSettingsChanged;
        Closed += (_, _) => services.Settings.Changed -= OnSettingsChanged;

        void OnSettingsChanged(object? sender, EventArgs e) =>
            RootGrid.RequestedTheme = Ui.ToElementTheme(services.Settings.Current.Theme);
    }

    public SettingsViewModel ViewModel { get; }

    // A Click a Command előtt fut. A NumberBox csak fókuszvesztéskor veszi át a beírt szöveget,
    // ezért a még be nem véglegesített értékeket itt írjuk át, hogy a Mentés biztosan azokat mentse.
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        foreach (var box in new[] { HeightBox, WidthBox, RefreshBox })
        {
            if (double.TryParse(box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var value))
            {
                box.Value = Math.Clamp(value, box.Minimum, box.Maximum);
            }
        }
    }

    public void BringToFront()
    {
        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);
    }
}
