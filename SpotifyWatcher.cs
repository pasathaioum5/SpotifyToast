using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SpotifyToast;

public sealed record Track(string Artist, string Title);

/// <summary>
/// Watches the Spotify desktop window title using WinEvent hooks (no polling).
/// While a track is playing Spotify sets its title to "Artist - Song"; when paused
/// or idle it shows "Spotify", "Spotify Free", "Spotify Premium", etc.
/// </summary>
/// <remarks>
/// Must be created and started on a thread with a message loop (the WinForms UI thread):
/// out-of-context WinEvent callbacks are delivered through that thread's message queue,
/// so <see cref="TrackChanged"/> is always raised on the UI thread.
/// </remarks>
public sealed class SpotifyWatcher : IDisposable
{
    private const string Separator = " - ";

    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int OBJID_WINDOW = 0;
    private const int CHILDID_SELF = 0;

    // Kept in a field so the GC never collects the delegate while native code holds it.
    private readonly WinEventProc _callback;
    private readonly List<IntPtr> _hooks = new();

    // Spotify emits several title changes in quick succession when skipping tracks;
    // coalesce them and then read the final title once.
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 250 };

    // pid -> "is Spotify.exe". Avoids opening a process handle for every name-change event.
    private readonly Dictionary<uint, bool> _pidCache = new();

    private IntPtr _spotifyHwnd;
    private string? _lastTitle;

    /// <summary>Raised on the UI thread when a new track starts playing.</summary>
    public event EventHandler<Track>? TrackChanged;

    public SpotifyWatcher()
    {
        _callback = OnWinEvent;
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Refresh();
        };
    }

    public void Start()
    {
        if (_hooks.Count > 0)
            return;

        // Two narrow hooks rather than one range: the range between them includes
        // EVENT_OBJECT_LOCATIONCHANGE, which fires constantly (e.g. on every mouse move).
        foreach (uint evt in new[] { EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_DESTROY })
        {
            IntPtr hook = SetWinEventHook(evt, evt, IntPtr.Zero, _callback, 0, 0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            if (hook == IntPtr.Zero)
                throw new InvalidOperationException($"SetWinEventHook failed for event 0x{evt:X}.");
            _hooks.Add(hook);
        }

        Refresh(); // pick up whatever is already playing
    }

    public void Stop()
    {
        _debounce.Stop();
        foreach (IntPtr hook in _hooks)
            UnhookWinEvent(hook);
        _hooks.Clear();
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF || hwnd == IntPtr.Zero)
            return;

        if (eventType == EVENT_OBJECT_DESTROY)
        {
            // The window is already gone, so its pid can't be queried: compare handles.
            if (hwnd == _spotifyHwnd)
                ScheduleRefresh();
            return;
        }

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid != 0 && IsSpotifyProcess(pid))
            ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private bool IsSpotifyProcess(uint pid)
    {
        if (_pidCache.TryGetValue(pid, out bool isSpotify))
            return isSpotify;

        // Bound the cache; pids get reused over time anyway.
        if (_pidCache.Count > 512)
            _pidCache.Clear();

        try
        {
            using var p = Process.GetProcessById((int)pid);
            isSpotify = string.Equals(p.ProcessName, "Spotify", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false; // process already exited; don't cache
        }

        _pidCache[pid] = isSpotify;
        return isSpotify;
    }

    private void Refresh()
    {
        string? title;
        try
        {
            (title, _spotifyHwnd) = FindSpotifyTrackWindow();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SpotifyWatcher: {ex}");
            return;
        }

        // Paused / closed: remember that, so resuming the same song toasts again.
        if (title is null)
        {
            _lastTitle = null;
            return;
        }

        if (title == _lastTitle)
            return;

        _lastTitle = title;

        // Split on the first separator only: song names often contain " - " ("Song - Remastered").
        int idx = title.IndexOf(Separator, StringComparison.Ordinal);
        var track = new Track(title[..idx].Trim(), title[(idx + Separator.Length)..].Trim());
        TrackChanged?.Invoke(this, track);
    }

    /// <summary>
    /// Returns Spotify's "Artist - Song" title and its window handle, or (null, 0)
    /// if Spotify is not running or nothing is playing.
    /// </summary>
    private static (string? Title, IntPtr Hwnd) FindSpotifyTrackWindow()
    {
        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName("Spotify"))
        {
            pids.Add((uint)p.Id);
            p.Dispose();
        }
        if (pids.Count == 0)
            return (null, IntPtr.Zero);

        string? found = null;
        IntPtr foundHwnd = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (!pids.Contains(pid))
                return true;

            string title = GetWindowTitle(hWnd);
            // Spotify owns several helper windows ("GDI+ Window", "Default IME", ...);
            // only the main window uses the "Artist - Song" format.
            if (title.Contains(Separator, StringComparison.Ordinal))
            {
                found = title;
                foundHwnd = hWnd;
                return false; // stop enumerating
            }
            return true;
        }, IntPtr.Zero);

        return (found, foundHwnd);
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        int len = GetWindowTextLength(hWnd);
        if (len == 0)
            return string.Empty;

        var sb = new StringBuilder(len + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public void Dispose()
    {
        Stop();
        _debounce.Dispose();
    }

    // --- Win32 ---------------------------------------------------------------

    private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);
}
