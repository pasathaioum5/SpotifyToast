using System.Diagnostics;
using System.Reflection;

namespace SpotifyToast;

/// <summary>Shows the app version, how it was made, and credits.</summary>
public sealed class AboutForm : Form
{
    private const string ToastifyUrl = "https://github.com/aleab/toastify";
    private const string ClaudeUrl = "https://claude.com";
    private const int TextWidth = 360;

    private readonly PictureBox _logo = new()
    {
        Size = new Size(48, 48),
        SizeMode = PictureBoxSizeMode.Zoom,
        Margin = new Padding(0, 4, 16, 0),
    };

    public AboutForm(Icon? icon)
    {
        Text = "About SpotifyToast";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        Font = new Font("Segoe UI", 9F);

        var text = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Margin = Padding.Empty,
        };
        text.Controls.Add(MakeLabel("SpotifyToast", new Font("Segoe UI Semibold", 14F), bottom: 0));
        text.Controls.Add(MakeLabel($"Version {GetVersion()}", bottom: 10));
        text.Controls.Add(MakeLabel("Shows a toast notification when the Spotify desktop app changes track."));

        text.Controls.Add(MakeLabel("Vibe coded with Claude", new Font("Segoe UI Semibold", 9F), bottom: 2));
        text.Controls.Add(MakeLink(
            "This app was vibe coded: it was built by describing each feature in plain language " +
            "to Claude, Anthropic's AI assistant, which wrote the code.",
            "Claude", ClaudeUrl));

        text.Controls.Add(MakeLabel("Credits", new Font("Segoe UI Semibold", 9F), bottom: 2));
        text.Controls.Add(MakeLink("Inspired by Toastify by aleab.", "Toastify", ToastifyUrl, bottom: 4));
        text.Controls.Add(MakeLabel(
            "Icon: ToastifyIcon.ico from the Toastify project (GPLv2), derived from the Spotify logo."));
        text.Controls.Add(MakeLabel(
            "Not affiliated with Spotify. Spotify is a trademark of Spotify AB.", bottom: 0));

        var close = new Button
        {
            Text = "Close",
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 16, 0, 0),
        };
        close.Click += (_, _) => Close();
        AcceptButton = CancelButton = close;

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        grid.Controls.Add(_logo, 0, 0);
        grid.Controls.Add(text, 1, 0);
        grid.Controls.Add(close, 1, 1);
        Controls.Add(grid);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Pick the icon frame for the real on-screen size so the logo stays sharp at any DPI.
        int px = _logo.Width;
        using var stream = typeof(AboutForm).Assembly.GetManifestResourceStream("SpotifyToast.app.ico");
        if (stream is not null)
        {
            using var large = new Icon(stream, px, px);
            _logo.Image = large.ToBitmap();
        }
    }

    /// <summary>"1.0.7" from the informational version, without the "+commit" suffix.</summary>
    private static string GetVersion()
    {
        // Read this assembly's version, not the entry assembly's (Application.ProductVersion).
        string version = typeof(AboutForm).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        int plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }

    private static Label MakeLabel(string text, Font? font = null, int bottom = 10) => new()
    {
        Text = text,
        Font = font,
        AutoSize = true,
        MaximumSize = new Size(TextWidth, 0),
        Margin = new Padding(0, 0, 0, bottom),
    };

    private LinkLabel MakeLink(string text, string linkText, string url, int bottom = 10)
    {
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(TextWidth, 0),
            Margin = new Padding(0, 0, 0, bottom),
        };
        link.Links.Clear();
        link.Links.Add(text.IndexOf(linkText, StringComparison.Ordinal), linkText.Length, url);
        link.LinkClicked += (_, e) => OpenUrl((string)e.Link!.LinkData!);
        return link;
    }

    private void OpenUrl(string url)
    {
        try
        {
            // Only the fixed https URLs above are ever opened, in the default browser.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, $"Could not open {url}:\n{ex.Message}", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _logo.Image?.Dispose();
        base.Dispose(disposing);
    }
}
