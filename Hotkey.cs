using System.Runtime.InteropServices;

namespace PathJump;

/// <summary>Converts between hotkey text like "Alt+~" and the values RegisterHotKey needs.</summary>
static class Hotkey
{
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

    public static bool TryParse(string s, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        foreach (var part in (s ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= Native.MOD_CONTROL; break;
                case "alt": mods |= Native.MOD_ALT; break;
                case "shift": mods |= Native.MOD_SHIFT; break;
                case "win": mods |= Native.MOD_WIN; break;
                case "~": case "`": case "tilde": case "backtick": vk = (uint)Keys.Oemtilde; break;
                default:
                    if (part.Length == 1 && char.IsDigit(part[0])) { vk = (uint)(Keys.D0 + (part[0] - '0')); break; }
                    if (part.All(char.IsDigit)) return false; // "12" would otherwise parse as a raw key code
                    if (!Enum.TryParse<Keys>(part, true, out var key)) return false;
                    vk = (uint)key;
                    break;
            }
        }
        return vk != 0;
    }

    /// <summary>Text for a key press captured in a form, e.g. "Ctrl+Alt+V". Null if only modifiers are down.</summary>
    public static string Format(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (IsModifier(key)) return null;
        var parts = new List<string>();
        if ((GetAsyncKeyState((int)Keys.LWin) & 0x8000) != 0 || (GetAsyncKeyState((int)Keys.RWin) & 0x8000) != 0) parts.Add("Win");
        if ((keyData & Keys.Control) != 0) parts.Add("Ctrl");
        if ((keyData & Keys.Alt) != 0) parts.Add("Alt");
        if ((keyData & Keys.Shift) != 0) parts.Add("Shift");
        parts.Add(KeyName(key));
        return string.Join("+", parts);
    }

    static string KeyName(Keys k) => k switch
    {
        Keys.Oemtilde => "~",
        >= Keys.D0 and <= Keys.D9 => ((int)(k - Keys.D0)).ToString(),
        _ => k.ToString(),
    };

    static bool IsModifier(Keys k) => k is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
        or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or Keys.Menu or Keys.LMenu or Keys.RMenu
        or Keys.LWin or Keys.RWin or Keys.None;

    /// <summary>Plain letters/digits without Ctrl/Alt/Win would block normal typing everywhere.</summary>
    public static bool NeedsModifier(string hotkey)
    {
        if (!TryParse(hotkey, out var mods, out var vk)) return true;
        bool functionKey = vk >= (uint)Keys.F1 && vk <= (uint)Keys.F24;
        return !functionKey && (mods & (Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_WIN)) == 0;
    }
}
