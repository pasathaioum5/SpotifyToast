namespace SpotifyToast;

/// <summary>
/// Read-only text box that records a key combination: focus it and press the keys.
/// Backspace/Delete clears it; Tab and Esc keep their normal dialog behavior.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    private const string NonePlaceholder = "None (click and press keys)";
    private Hotkey? _value;

    public event EventHandler? ValueChanged;

    public HotkeyBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;
        Cursor = Cursors.Hand;
        ShortcutsEnabled = false; // no Ctrl+C/V context handling; every key is a candidate
        UpdateText();
    }

    public Hotkey? Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;
            _value = value;
            UpdateText();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        Keys mods = keyData & Keys.Modifiers;

        // Let plain Tab / Shift+Tab / Esc move focus or cancel the dialog as usual.
        if (mods is Keys.None or Keys.Shift && key == Keys.Tab || mods == Keys.None && key == Keys.Escape)
            return base.ProcessCmdKey(ref msg, keyData);

        if (mods == Keys.None && key is Keys.Back or Keys.Delete)
        {
            Value = null;
            return true;
        }

        if (Hotkey.IsModifierKey(key))
        {
            // Show the modifiers held so far, e.g. "Ctrl+Alt+…".
            Text = string.Concat(
                mods.HasFlag(Keys.Control) ? "Ctrl+" : "",
                mods.HasFlag(Keys.Alt) ? "Alt+" : "",
                mods.HasFlag(Keys.Shift) ? "Shift+" : "") + "…";
            return true;
        }

        var candidate = new Hotkey(key, mods);
        if (candidate.IsValid)
            Value = candidate;
        UpdateText(); // also restores the text if the combination was rejected
        return true;  // swallow so Alt+letter doesn't trigger mnemonics / beep
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        // Released the modifiers without completing a combination: show the saved value again.
        if (Control.ModifierKeys == Keys.None)
            UpdateText();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        UpdateText();
    }

    private void UpdateText() => Text = _value?.ToString() ?? NonePlaceholder;
}
