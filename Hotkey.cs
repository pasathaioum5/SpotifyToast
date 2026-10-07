using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SpotifyToast;

/// <summary>
/// A key plus Ctrl/Alt/Shift modifiers, stored as an invariant string like "Ctrl+Alt+S".
/// </summary>
public readonly record struct Hotkey(Keys Key, Keys Modifiers)
{
    /// <summary>Accepts "Ctrl+Alt+S", "ctrl + shift + f12", "Alt+1", etc. Order of modifiers is free.</summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        Keys mods = Keys.None, key = Keys.None;
        foreach (string raw in text.Split('+'))
        {
            string part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= Keys.Control; break;
                case "alt": mods |= Keys.Alt; break;
                case "shift": mods |= Keys.Shift; break;
                default:
                    if (key != Keys.None)
                        return false; // two non-modifier keys
                    // Allow plain digits ("1") as well as enum names ("D1").
                    if (part.Length == 1 && char.IsAsciiDigit(part[0]))
                        part = "D" + part;
                    if (!Enum.TryParse(part, ignoreCase: true, out key) || IsModifierKey(key))
                        return false;
                    break;
            }
        }

        hotkey = new Hotkey(key, mods);
        return hotkey.IsValid;
    }

    /// <summary>
    /// Plain letters/digits without a modifier would hijack normal typing everywhere,
    /// so those require at least one modifier; F-keys and similar may stand alone.
    /// </summary>
    public bool IsValid =>
        Key != Keys.None && !IsModifierKey(Key) &&
        (Modifiers != Keys.None || Key is >= Keys.F1 and <= Keys.F24 or Keys.Pause or Keys.Scroll);

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(Keys.Shift)) parts.Add("Shift");
        parts.Add(Key is >= Keys.D0 and <= Keys.D9 ? ((char)('0' + (Key - Keys.D0))).ToString() : Key.ToString());
        return string.Join("+", parts);
    }

    public static bool IsModifierKey(Keys key) => key is
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin;
}

/// <summary>
/// Registers one system-wide hotkey via RegisterHotKey and raises <see cref="Pressed"/>
/// on the UI thread. Uses a hidden message-only window to receive WM_HOTKEY.
/// </summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 1;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private bool _registered;

    public event EventHandler? Pressed;

    public HotkeyManager()
    {
        CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
    }

    /// <summary>
    /// Replaces the current hotkey. Returns false (with the Win32 reason) if another
    /// application already owns that combination; the previous hotkey is then gone.
    /// </summary>
    public bool Register(Hotkey hotkey, out string? error)
    {
        Unregister();
        error = null;

        uint mods = MOD_NOREPEAT; // holding the keys down shouldn't spam toasts
        if (hotkey.Modifiers.HasFlag(Keys.Control)) mods |= MOD_CONTROL;
        if (hotkey.Modifiers.HasFlag(Keys.Alt)) mods |= MOD_ALT;
        if (hotkey.Modifiers.HasFlag(Keys.Shift)) mods |= MOD_SHIFT;

        if (!RegisterHotKey(Handle, HotkeyId, mods, (uint)hotkey.Key))
        {
            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        _registered = true;
        return true;
    }

    public void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && (int)m.WParam == HotkeyId)
            Pressed?.Invoke(this, EventArgs.Empty);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
