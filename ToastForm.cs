using System.Diagnostics;

namespace SpotifyToast;

/// <summary>
/// Small borderless, always-on-top popup that fades in, waits, then fades out.
/// Never steals focus or appears in the taskbar. Text is custom-painted so it
/// scales with whatever size the user picks in settings.
/// </summary>
/// <remarks>
/// A song title or artist too wide for the toast scrolls once as a marquee: pause at the
/// start, scroll until the end is visible, then stay there. The toast never fades out
/// before that pass (plus a closing pause) completes, so long text is always read in
/// full, even if that keeps the toast up longer than the configured duration.
/// </remarks>
public sealed class ToastForm : Form
{
    private const double FadeStep = 0.1;
    private const int PaddingLogical = 14;
    private const int MarqueePauseMs = 1000; // dwell at the start and at the end of each pass

    private const TextFormatFlags BaseFlags = TextFormatFlags.Left | TextFormatFlags.SingleLine |
                                              TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    private readonly System.Windows.Forms.Timer _fadeTimer = new() { Interval = 20 };
    // Drives both the hold countdown and the marquee; ~60 fps while visible, stopped otherwise.
    private readonly System.Windows.Forms.Timer _animTimer = new() { Interval = 15 };
    private readonly Stopwatch _holdClock = new();
    private AppSettings _settings = new();
    private Track _track = new("", "");
    private bool _fadingIn;

    // Layout cache (device pixels), rebuilt when the text, size, settings or DPI change.
    private Font? _titleFont, _artistFont;
    private Rectangle _titleRect, _artistRect;
    private int _titleOverflow, _artistOverflow; // how far each line must scroll; 0 = fits
    private int _titleOffset, _artistOffset;     // current scroll position

    public ToastForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None; // we scale manually from logical settings
        DoubleBuffered = true;
        Opacity = 0;

        Click += (_, _) => BeginFadeOut(); // click to dismiss
        _fadeTimer.Tick += OnFadeTick;
        _animTimer.Tick += OnAnimTick;

        // Create the (hidden) window now so DeviceDpi is the real monitor DPI on first show.
        CreateHandle();
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings.Clone();
        BackColor = _settings.Background;
        if (Visible)
        {
            Reposition();
            UpdateLayout();
            Invalidate();
        }
    }

    public void ShowTrack(Track track)
    {
        _track = track;
        Reposition();
        UpdateLayout();
        Invalidate();

        _animTimer.Stop();
        _holdClock.Reset(); // the hold countdown starts once fully faded in
        _fadingIn = true;
        if (!Visible)
            Show();
        _fadeTimer.Start();
    }

    private void Reposition()
    {
        var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        var area = screen.WorkingArea;
        float scale = DeviceDpi / 96f;

        int w = (int)(_settings.Width * scale);
        int h = (int)(_settings.Height * scale);
        int mx = (int)(_settings.MarginX * scale);
        int my = (int)(_settings.MarginY * scale);

        int x = _settings.Corner is ToastCorner.BottomLeft or ToastCorner.TopLeft
            ? area.Left + mx
            : area.Right - w - mx;
        int y = _settings.Corner is ToastCorner.TopLeft or ToastCorner.TopRight
            ? area.Top + my
            : area.Bottom - h - my;

        // Keep the toast on-screen even with large margins.
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - w));
        y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - h));

        Bounds = new Rectangle(x, y, w, h);
    }

    /// <summary>Measures fonts, line rectangles and how far each line overflows.</summary>
    private void UpdateLayout()
    {
        _titleFont?.Dispose();
        _artistFont?.Dispose();
        _titleFont = _artistFont = null;
        _titleOverflow = _artistOverflow = _titleOffset = _artistOffset = 0;

        int pad = (int)(PaddingLogical * DeviceDpi / 96f);
        var content = Rectangle.Inflate(ClientRectangle, -pad, -pad);
        if (content.Width <= 0 || content.Height <= 0)
            return;

        // Font sizes follow the toast height so small/large toasts both look balanced.
        float titlePx = Math.Clamp(content.Height * 0.36f, 10f, 48f);
        float artistPx = Math.Clamp(content.Height * 0.27f, 9f, 36f);
        _titleFont = new Font("Segoe UI Semibold", titlePx, GraphicsUnit.Pixel);
        _artistFont = new Font("Segoe UI", artistPx, GraphicsUnit.Pixel);

        Size titleSize = TextRenderer.MeasureText(_track.Title.Length > 0 ? _track.Title : "Ag", _titleFont, Size.Empty, BaseFlags);
        Size artistSize = TextRenderer.MeasureText(_track.Artist.Length > 0 ? _track.Artist : "Ag", _artistFont, Size.Empty, BaseFlags);
        int gap = (int)(titlePx * 0.15f);
        int top = content.Top + Math.Max(0, (content.Height - titleSize.Height - gap - artistSize.Height) / 2);

        _titleRect = new Rectangle(content.Left, top, content.Width, titleSize.Height);
        _artistRect = new Rectangle(content.Left, top + titleSize.Height + gap, content.Width, artistSize.Height);
        _titleOverflow = Math.Max(0, titleSize.Width - content.Width);
        _artistOverflow = Math.Max(0, artistSize.Width - content.Width);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Reposition();
        UpdateLayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_titleFont is null || _artistFont is null)
            return;

        DrawLine(e.Graphics, _track.Title, _titleFont, _titleRect, _titleOverflow, _titleOffset, _settings.Title);
        DrawLine(e.Graphics, _track.Artist, _artistFont, _artistRect, _artistOverflow, _artistOffset, _settings.Artist);
    }

    private static void DrawLine(Graphics g, string text, Font font, Rectangle bounds,
        int overflow, int offset, Color color)
    {
        if (overflow == 0)
        {
            TextRenderer.DrawText(g, text, font, bounds, color, BaseFlags);
            return;
        }

        // Draw the full-width text shifted left, clipped to the line's box.
        var state = g.Save();
        g.SetClip(bounds);
        var shifted = new Rectangle(bounds.Left - offset, bounds.Top, bounds.Width + overflow + 1, bounds.Height);
        TextRenderer.DrawText(g, text, font, shifted, color, BaseFlags | TextFormatFlags.PreserveGraphicsClipping);
        g.Restore(state);
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        long elapsed = _holdClock.ElapsedMilliseconds;
        int maxOverflow = Math.Max(_titleOverflow, _artistOverflow);
        long holdMs = (long)(_settings.DurationSeconds * 1000);

        if (maxOverflow == 0)
        {
            if (elapsed >= holdMs)
                BeginFadeOut();
            return;
        }

        // A single marquee pass: pause, scroll the longest line fully into view, pause.
        double pxPerMs = _settings.MarqueeSpeed * DeviceDpi / 96.0 / 1000.0;
        long passMs = MarqueePauseMs + (long)Math.Ceiling(maxOverflow / pxPerMs) + MarqueePauseMs;
        // Stay for the configured duration, or longer if the pass needs it. The text scrolls
        // only once; any remaining time is spent showing the end of the text.
        if (elapsed >= Math.Max(holdMs, passMs))
        {
            BeginFadeOut();
            return;
        }

        double scrolled = elapsed < MarqueePauseMs ? 0 : (elapsed - MarqueePauseMs) * pxPerMs;
        // Both lines move at the same speed; each stops at its own end and stays there.
        SetOffsets((int)Math.Min(scrolled, _titleOverflow), (int)Math.Min(scrolled, _artistOverflow));
    }

    private void SetOffsets(int title, int artist)
    {
        if (title == _titleOffset && artist == _artistOffset)
            return;
        _titleOffset = title;
        _artistOffset = artist;
        Invalidate();
    }

    private void BeginFadeOut()
    {
        _animTimer.Stop();
        _holdClock.Stop();
        _fadingIn = false;
        _fadeTimer.Start();
    }

    private void OnFadeTick(object? sender, EventArgs e)
    {
        if (_fadingIn)
        {
            Opacity = Math.Min(1, Opacity + FadeStep);
            if (Opacity >= 1)
            {
                _fadeTimer.Stop();
                _holdClock.Restart();
                _animTimer.Start();
            }
        }
        else
        {
            Opacity = Math.Max(0, Opacity - FadeStep);
            if (Opacity <= 0)
            {
                _fadeTimer.Stop();
                Hide();
            }
        }
    }

    // Show without activating so the toast never steals keyboard focus.
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_NOACTIVATE = 0x08000000;
            const int WS_EX_TOPMOST = 0x00000008;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fadeTimer.Dispose();
            _animTimer.Dispose();
            _titleFont?.Dispose();
            _artistFont?.Dispose();
        }
        base.Dispose(disposing);
    }
}
