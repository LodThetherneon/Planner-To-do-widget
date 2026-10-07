using PlannerWidget.App.Interop;

namespace PlannerWidget.App.Services;

/// <summary>Gyorsbillentyű-leírás, pl. "Alt+W" vagy "Ctrl+Alt+P".</summary>
public sealed record Hotkey(uint Modifiers, uint VirtualKey, string Display)
{
    public const string Disabled = "Kikapcsolva";

    /// <summary>A beállításokban felkínált lehetőségek.</summary>
    public static readonly string[] Presets =
    [
        "Alt+W",
        "Ctrl+Alt+P",
        "Ctrl+Shift+Space",
        "Win+Shift+P",
        "Ctrl+Alt+T",
        Disabled,
    ];

    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == Disabled)
        {
            return null;
        }

        uint modifiers = 0;
        uint key = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "ALT": modifiers |= NativeMethods.MOD_ALT; break;
                case "CTRL" or "CONTROL": modifiers |= NativeMethods.MOD_CONTROL; break;
                case "SHIFT": modifiers |= NativeMethods.MOD_SHIFT; break;
                case "WIN": modifiers |= NativeMethods.MOD_WIN; break;
                case "SPACE": key = 0x20; break;
                case var s when s.Length == 1 && char.IsAsciiLetterOrDigit(s[0]): key = s[0]; break;
                case var s when s.StartsWith('F') && int.TryParse(s[1..], out var f) && f is >= 1 and <= 12:
                    key = (uint)(0x70 + f - 1);
                    break;
                default: return null;
            }
        }

        return key == 0 || modifiers == 0 ? null : new Hotkey(modifiers, key, text);
    }
}
