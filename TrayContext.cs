using Microsoft.Win32;

namespace SpotifyToast;

/// <summary>
/// Application context with no main window: just a tray icon, the Spotify watcher,
/// the global show-song hotkey and the toast popup.
/// </summary>
public sealed class TrayContext : ApplicationContext
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "SpotifyToast";
    private static readonly Track SampleTrack = new("Artist Name", "Song Title");
    private static readonly Track NothingPlaying = new("Spotify", "Nothing is playing");

    private readonly NotifyIcon _tray;
    private readonly Icon _appIcon;
    private readonly SpotifyWatcher _watcher = new();
    private readonly ToastForm _toast = new();
    private readonly HotkeyManager _hotkeys = new();
    private readonly ToolStripMenuItem _showItem;
    private AppSettings _settings = AppSettings.Load();
    private SettingsForm? _settingsForm;
    private Track? _current;

    public TrayContext()
    {
        _appIcon = LoadAppIcon();
        _toast.ApplySettings(_settings);

        var startup = new ToolStripMenuItem("Start with Windows") { Checked = IsStartupEnabled() };
        startup.Click += (_, _) => startup.Checked = ToggleStartup(!startup.Checked);

        var menu = new ContextMenuStrip();
        _showItem = new ToolStripMenuItem("Show current song", null, (_, _) => ShowCurrent());
        menu.Items.Add(_showItem);
        menu.Items.Add("Settings...", null, (_, _) => OpenSettings());
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "SpotifyToast",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowCurrent(); };
        _tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenSettings(); };

        _hotkeys.Pressed += (_, _) => ShowCurrent(fromHotkey: true);
        if (TryApplyHotkey(_settings) is { } hotkeyError)
        {
            // Non-blocking: the app is still useful without the shortcut.
            _tray.ShowBalloonTip(5000, "SpotifyToast", hotkeyError, ToolTipIcon.Warning);
        }

        _watcher.TrackChanged += (_, track) =>
        {
            _current = track;
            _tray.Text = Truncate($"{track.Artist} - {track.Title}", 127);
            _toast.ShowTrack(track);
        };

        try
        {
            _watcher.Start();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show($"Could not start listening for Spotify:\n{ex.Message}", "SpotifyToast",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowCurrent(bool fromHotkey = false)
    {
        if (_current is not null)
            _toast.ShowTrack(_current);
        else if (fromHotkey)
            _toast.ShowTrack(NothingPlaying); // give feedback that the shortcut worked
    }

    /// <summary>Registers the settings' hotkey. Returns a user-facing error, or null on success.</summary>
    private string? TryApplyHotkey(AppSettings settings)
    {
        _hotkeys.Unregister();
        _showItem.ShortcutKeyDisplayString = null;

        if (string.IsNullOrEmpty(settings.ShowHotkey))
            return null; // disabled

        if (!Hotkey.TryParse(settings.ShowHotkey, out var hotkey))
            return $"\"{settings.ShowHotkey}\" is not a valid shortcut.";

        if (!_hotkeys.Register(hotkey, out string? error))
            return $"The shortcut {hotkey} is already used by another application ({error}). " +
                   "Choose a different one in Settings.";

        _showItem.ShortcutKeyDisplayString = hotkey.ToString();
        return null;
    }

    private void OpenSettings()
    {
        if (_settingsForm is not null)
        {
            _settingsForm.Activate();
            return;
        }

        // Release the global hotkey while editing so the shortcut box can capture it.
        _hotkeys.Unregister();

        _settingsForm = new SettingsForm(_settings,
            preview: preview =>
            {
                _toast.ApplySettings(preview);
                _toast.ShowTrack(_current ?? SampleTrack);
            },
            // Runs on Save: registering here doubles as the "is it free?" check.
            validate: TryApplyHotkey,
            _appIcon);

        try
        {
            if (_settingsForm.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    _settingsForm.Result.Save();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    MessageBox.Show($"Could not save settings:\n{ex.Message}\n\nChanges apply until you exit.",
                        "SpotifyToast", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                _settings = _settingsForm.Result;
            }
            else
            {
                TryApplyHotkey(_settings); // restore the previous shortcut
            }
            // On cancel this reverts any live-previewed changes.
            _toast.ApplySettings(_settings);
        }
        finally
        {
            _settingsForm.Dispose();
            _settingsForm = null;
        }
    }

    private static Icon LoadAppIcon()
    {
        using var stream = typeof(TrayContext).Assembly.GetManifestResourceStream("SpotifyToast.app.ico");
        // Ask for the system small-icon size so Windows picks the native 16/20/24px frame.
        return stream is not null
            ? new Icon(stream, SystemInformation.SmallIconSize)
            : (Icon)SystemIcons.Application.Clone();
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is not null;
    }

    /// <summary>Adds/removes the per-user (HKCU) Run entry. Returns the resulting state.</summary>
    private bool ToggleStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enable)
                key.SetValue(RunValueName, $"\"{Application.ExecutablePath}\"");
            else
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not update startup setting:\n{ex.Message}", "SpotifyToast",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return IsStartupEnabled();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    protected override void ExitThreadCore()
    {
        _settingsForm?.Close();
        _watcher.Stop();
        _tray.Visible = false; // remove the icon immediately instead of leaving a ghost
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watcher.Dispose();
            _hotkeys.Dispose();
            _toast.Dispose();
            _tray.Dispose();
            _appIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
