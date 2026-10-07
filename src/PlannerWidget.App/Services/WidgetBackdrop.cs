using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace PlannerWidget.App.Services;

/// <summary>
/// A widget akril (elmosott) háttere, beállítható viselkedéssel. A beépített DesktopAcrylicBackdrop
/// inaktív ablaknál (ha máshová kattintasz) mindig egyszínűre vált; itt mindkét állapot külön kapcsolható.
/// „Nem elmosott” = az akril saját, egyszínű tartalék színe (pont olyan, mint eddig inaktívan).
/// </summary>
public sealed partial class WidgetBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _config;
    private bool _windowActive = true;
    private bool _blurWhenActive = true;
    private bool _blurWhenInactive;
    private ElementTheme _theme = ElementTheme.Default;

    public void Configure(bool blurWhenActive, bool blurWhenInactive)
    {
        _blurWhenActive = blurWhenActive;
        _blurWhenInactive = blurWhenInactive;
        Update();
    }

    public void SetWindowActive(bool active)
    {
        _windowActive = active;
        Update();
    }

    public void SetTheme(ElementTheme theme)
    {
        _theme = theme;
        Update();
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        if (!DesktopAcrylicController.IsSupported())
        {
            return;
        }

        _config = new SystemBackdropConfiguration();
        _controller = new DesktopAcrylicController();
        _controller.SetSystemBackdropConfiguration(_config);
        _controller.AddSystemBackdropTarget(connectedTarget);
        Update();
    }

    /// <summary>
    /// Témaváltáskor a keretrendszer ezt hívja. Az alaposztály a saját (alapértelmezett) konfigurációját frissítené,
    /// de mi saját <see cref="SystemBackdropConfiguration"/>-t használunk – az alap hívása ArgumentException-t dob
    /// („target”). A témát a <see cref="SetTheme"/> követi (RootGrid.ActualThemeChanged).
    /// </summary>
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        if (_controller is not null)
        {
            _controller.RemoveSystemBackdropTarget(disconnectedTarget);
            _controller.Dispose();
            _controller = null;
        }

        _config = null;
    }

    private void Update()
    {
        if (_config is null)
        {
            return;
        }

        // Az akril kontroller az IsInputActive=false állapotban a tartalék (egyszínű) színt mutatja.
        _config.IsInputActive = _windowActive ? _blurWhenActive : _blurWhenInactive;
        _config.Theme = _theme switch
        {
            ElementTheme.Dark => SystemBackdropTheme.Dark,
            ElementTheme.Light => SystemBackdropTheme.Light,
            _ => SystemBackdropTheme.Default,
        };
    }
}
