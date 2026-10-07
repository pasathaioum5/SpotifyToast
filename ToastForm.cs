namespace SpotifyToast;

/// <summary>
/// Small borderless, always-on-top popup that fades in, waits, then fades out.
/// Never steals focus or appears in the taskbar. Text is custom-painted so it
/// scales with whatever size the user picks in settings.
/// </summary>
public sealed class ToastForm : Form
{
    private const double FadeStep = 0.1;
    private const int PaddingLogical = 14;

    private readonly System.Windows.Forms.Timer _fadeTimer = new() { Interval = 20 };
    private readonly System.Windows.Forms.Timer _holdTimer = new();
    private AppSettings _settings = new();
    private Track _track = new("", "");
    private bool _fadingIn;

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
        _holdTimer.Tick += (_, _) => BeginFadeOut();

        // Create the (hidden) window now so DeviceDpi is the real monitor DPI on first show.
        CreateHandle();
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings.Clone();
        BackColor = _settings.Background;
        // Setting Interval restarts a running timer, so a visible toast gets the full new duration.
        _holdTimer.Interval = (int)(_settings.DurationSeconds * 1000);
        if (Visible)
        {
            Reposition();
            Invalidate();
        }
    }

    public void ShowTrack(Track track)
    {
        _track = track;
        Reposition();
        Invalidate();

        _holdTimer.Stop();
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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        int pad = (int)(PaddingLogical * DeviceDpi / 96f);
        var content = Rectangle.Inflate(ClientRectangle, -pad, -pad);
        if (content.Width <= 0 || content.Height <= 0)
            return;

        // Font sizes follow the toast height so small/large toasts both look balanced.
        float titlePx = Math.Clamp(content.Height * 0.36f, 10f, 48f);
        float artistPx = Math.Clamp(content.Height * 0.27f, 9f, 36f);
        using var titleFont = new Font("Segoe UI Semibold", titlePx, GraphicsUnit.Pixel);
        using var artistFont = new Font("Segoe UI", artistPx, GraphicsUnit.Pixel);

        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.SingleLine |
                                      TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                                      TextFormatFlags.NoPadding;

        int titleH = TextRenderer.MeasureText(e.Graphics, "Ag", titleFont, Size.Empty, flags).Height;
        int artistH = TextRenderer.MeasureText(e.Graphics, "Ag", artistFont, Size.Empty, flags).Height;
        int gap = (int)(titlePx * 0.15f);
        int top = content.Top + Math.Max(0, (content.Height - titleH - gap - artistH) / 2);

        TextRenderer.DrawText(e.Graphics, _track.Title, titleFont,
            new Rectangle(content.Left, top, content.Width, titleH), _settings.Title, flags);
        TextRenderer.DrawText(e.Graphics, _track.Artist, artistFont,
            new Rectangle(content.Left, top + titleH + gap, content.Width, artistH), _settings.Artist, flags);
    }

    private void BeginFadeOut()
    {
        _holdTimer.Stop();
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
                _holdTimer.Start();
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
            _holdTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
