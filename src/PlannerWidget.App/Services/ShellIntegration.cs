using System.Runtime.InteropServices;
using PlannerWidget.App.Interop;
using PlannerWidget.Core.Logging;
using static PlannerWidget.App.Interop.NativeMethods;

namespace PlannerWidget.App.Services;

public sealed record TrayMenuItem(int Id, string Text, bool IsEnabled = true, bool IsChecked = false)
{
    public static TrayMenuItem Separator { get; } = new(0, "");
    public bool IsSeparator => Id == 0;
}

/// <summary>
/// Tálcaikon (értesítési terület) és globális gyorsbillentyű egy rejtett Win32 ablakon keresztül.
/// Külső csomag nélkül, közvetlenül a Shell_NotifyIcon / RegisterHotKey API-val.
/// </summary>
public sealed class ShellIntegration : IDisposable
{
    private const string ClassName = "PlannerWidget.ShellWindow";
    private const int TrayCallbackMessage = WM_APP + 1;
    private const int HotkeyId = 0x5057;

    private readonly WndProc _wndProc; // Erős hivatkozás: a GC nem gyűjtheti be, amíg a natív ablak él.
    private readonly uint _taskbarCreatedMessage;
    private readonly string _iconPath;
    private readonly nint _hwnd;
    private nint _icon;
    private nint _largeIcon;
    private bool _trayAdded;
    private bool _hotkeyRegistered;
    private string _tooltip;

    public ShellIntegration(string iconPath, string tooltip)
    {
        _iconPath = iconPath;
        _tooltip = tooltip;
        _wndProc = WindowProcedure;
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        var hInstance = GetModuleHandle(null);
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = ClassName,
        };
        RegisterClassEx(ref wc);
        _hwnd = CreateWindowEx(WS_EX_TOOLWINDOW, ClassName, "PlannerWidget.Shell", WS_POPUP, 0, 0, 0, 0, 0, 0, hInstance, 0);
        if (_hwnd == 0)
        {
            Log.Error($"A tálca-ablak nem hozható létre (Win32 hiba {Marshal.GetLastWin32Error()}).");
        }
    }

    /// <summary>Bal kattintás a tálcaikonon.</summary>
    public event EventHandler? TrayClicked;

    /// <summary>A tálcamenü egy elemét választották (az elem azonosítója).</summary>
    public event EventHandler<int>? MenuItemInvoked;

    public event EventHandler? HotkeyPressed;

    /// <summary>A jobb klikkes menü tartalmát minden megnyitáskor újra lekérjük (pl. „Munka kezdete/vége”).</summary>
    public Func<IReadOnlyList<TrayMenuItem>>? MenuProvider { get; set; }

    public void ShowTrayIcon()
    {
        if (_hwnd == 0)
        {
            return;
        }

        if (_icon == 0)
        {
            _icon = LoadImage(0, _iconPath, IMAGE_ICON, GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_LOADFROMFILE);
        }

        var data = CreateData();
        _trayAdded = Shell_NotifyIcon(NIM_ADD, ref data);
        if (!_trayAdded)
        {
            Log.Warn("A tálcaikon nem adható hozzá.");
        }
    }

    /// <summary>Windows értesítés (Windows 10/11-en toastként jelenik meg) a tálcaikonon keresztül.</summary>
    public void ShowNotification(string title, string text)
    {
        if (!_trayAdded)
        {
            return;
        }

        if (_largeIcon == 0)
        {
            _largeIcon = LoadImage(0, _iconPath, IMAGE_ICON, 48, 48, LR_LOADFROMFILE);
        }

        var data = CreateData();
        data.uFlags |= NIF_INFO;
        data.szInfoTitle = title.Length > 63 ? title[..63] : title;
        data.szInfo = text.Length > 255 ? text[..255] : text;
        data.dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON;
        data.hBalloonIcon = _largeIcon;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    /// <summary>Az értesítésre kattintottak.</summary>
    public event EventHandler? NotificationClicked;

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        if (_trayAdded)
        {
            var data = CreateData();
            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }
    }

    /// <summary>Gyorsbillentyű regisztrálása; hamis, ha egy másik program már használja.</summary>
    public bool RegisterHotkey(Hotkey? hotkey)
    {
        UnregisterHotkey();
        if (hotkey is null || _hwnd == 0)
        {
            return true;
        }

        _hotkeyRegistered = NativeMethods.RegisterHotKey(_hwnd, HotkeyId, hotkey.Modifiers | MOD_NOREPEAT, hotkey.VirtualKey);
        if (!_hotkeyRegistered)
        {
            Log.Warn($"A(z) {hotkey.Display} gyorsbillentyű foglalt (Win32 hiba {Marshal.GetLastWin32Error()}).");
        }

        return _hotkeyRegistered;
    }

    public void UnregisterHotkey()
    {
        if (_hotkeyRegistered)
        {
            UnregisterHotKey(_hwnd, HotkeyId);
            _hotkeyRegistered = false;
        }
    }

    public void Dispose()
    {
        UnregisterHotkey();
        if (_trayAdded)
        {
            var data = CreateData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _trayAdded = false;
        }

        if (_icon != 0)
        {
            DestroyIcon(_icon);
            _icon = 0;
        }

        if (_largeIcon != 0)
        {
            DestroyIcon(_largeIcon);
            _largeIcon = 0;
        }

        if (_hwnd != 0)
        {
            DestroyWindow(_hwnd);
        }

        UnregisterClass(ClassName, GetModuleHandle(null));
    }

    private NOTIFYICONDATA CreateData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP,
        uCallbackMessage = TrayCallbackMessage,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    private nint WindowProcedure(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (msg == TrayCallbackMessage)
            {
                switch (LowWord(lParam))
                {
                    case WM_LBUTTONUP:
                        TrayClicked?.Invoke(this, EventArgs.Empty);
                        break;
                    case WM_RBUTTONUP or WM_CONTEXTMENU:
                        ShowContextMenu();
                        break;
                    case NIN_BALLOONUSERCLICK:
                        NotificationClicked?.Invoke(this, EventArgs.Empty);
                        break;
                }

                return 0;
            }

            if (msg == WM_HOTKEY && (int)wParam == HotkeyId)
            {
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
                return 0;
            }

            if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
            {
                // Az Intéző újraindult: az ikont újra fel kell venni.
                _trayAdded = false;
                ShowTrayIcon();
                return 0;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Hiba a tálca-üzenet kezelésében", ex);
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var items = MenuProvider?.Invoke() ?? [];
        if (items.Count == 0)
        {
            return;
        }

        var menu = CreatePopupMenu();
        try
        {
            foreach (var item in items)
            {
                if (item.IsSeparator)
                {
                    AppendMenu(menu, MF_SEPARATOR, 0, null);
                    continue;
                }

                var flags = MF_STRING | (item.IsEnabled ? 0 : MF_GRAYED) | (item.IsChecked ? MF_CHECKED : 0);
                AppendMenu(menu, flags, (nuint)item.Id, item.Text);
            }

            GetCursorPos(out var point);
            // A menü csak akkor tűnik el kattintásra máshol, ha az ablakunk az előtérben van.
            SetForegroundWindow(_hwnd);
            var command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, point.X, point.Y, _hwnd, 0);
            PostMessage(_hwnd, WM_NULL, 0, 0);
            if (command > 0)
            {
                MenuItemInvoked?.Invoke(this, command);
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }
}
