namespace SpotifyToast;

/// <summary>
/// Lets the user customize toast size, position, colors, duration and shortcut. Every
/// change is previewed live through <c>preview</c>; the caller decides whether to keep
/// or revert on close, and <c>validate</c> can veto Save (e.g. hotkey already taken).
/// </summary>
public sealed class SettingsForm : Form
{
    private static readonly (ToastCorner Value, string Text)[] Corners =
    {
        (ToastCorner.BottomRight, "Bottom right"),
        (ToastCorner.BottomLeft, "Bottom left"),
        (ToastCorner.TopRight, "Top right"),
        (ToastCorner.TopLeft, "Top left"),
    };

    private readonly Action<AppSettings> _preview;
    private readonly Func<AppSettings, string?> _validate;
    private readonly NumericUpDown _width = MakeNumber(AppSettings.MinWidth, AppSettings.MaxWidth);
    private readonly NumericUpDown _height = MakeNumber(AppSettings.MinHeight, AppSettings.MaxHeight);
    private readonly NumericUpDown _marginX = MakeNumber(0, AppSettings.MaxMargin);
    private readonly NumericUpDown _marginY = MakeNumber(0, AppSettings.MaxMargin);
    private readonly NumericUpDown _duration = new()
    {
        Minimum = (decimal)AppSettings.MinDuration,
        Maximum = (decimal)AppSettings.MaxDuration,
        DecimalPlaces = 1,
        Increment = 0.5m,
        Width = 80,
    };
    private readonly ComboBox _corner = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly Button _background = MakeColorButton();
    private readonly Button _titleColor = MakeColorButton();
    private readonly Button _artistColor = MakeColorButton();
    private readonly HotkeyBox _hotkey = new() { Width = 200 };
    private bool _loading;

    /// <summary>The edited settings; valid when the dialog returns <see cref="DialogResult.OK"/>.</summary>
    public AppSettings Result { get; private set; }

    public SettingsForm(AppSettings current, Action<AppSettings> preview,
        Func<AppSettings, string?> validate, Icon? icon)
    {
        Result = current.Clone();
        _preview = preview;
        _validate = validate;

        Text = "SpotifyToast Settings";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        Font = new Font("Segoe UI", 9F);

        foreach (var c in Corners)
            _corner.Items.Add(c.Text);

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        AddRow(grid, "Width (px)", _width);
        AddRow(grid, "Height (px)", _height);
        AddRow(grid, "Position", _corner);
        AddRow(grid, "Horizontal margin (px)", _marginX);
        AddRow(grid, "Vertical margin (px)", _marginY);
        AddRow(grid, "Display duration (seconds)", _duration);
        AddRow(grid, "Background color", _background);
        AddRow(grid, "Song title color", _titleColor);
        AddRow(grid, "Artist color", _artistColor);

        var clearHotkey = new Button { Text = "Clear", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        clearHotkey.Click += (_, _) => _hotkey.Value = null;
        var hotkeyRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        hotkeyRow.Controls.AddRange(new Control[] { _hotkey, clearHotkey });
        AddRow(grid, "Show-song shortcut", hotkeyRow);

        var previewButton = new Button { Text = "Preview", AutoSize = true };
        var resetButton = new Button { Text = "Reset to defaults", AutoSize = true };
        var saveButton = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 12, 0, 0),
        };
        buttons.Controls.AddRange(new Control[] { previewButton, resetButton, saveButton, cancelButton });
        grid.Controls.Add(buttons, 0, grid.RowCount);
        grid.SetColumnSpan(buttons, 2);
        Controls.Add(grid);

        LoadValues(Result);

        foreach (var n in new[] { _width, _height, _marginX, _marginY, _duration })
            n.ValueChanged += (_, _) => OnChanged();
        _corner.SelectedIndexChanged += (_, _) => OnChanged();
        _background.Click += (_, _) => PickColor(_background);
        _titleColor.Click += (_, _) => PickColor(_titleColor);
        _artistColor.Click += (_, _) => PickColor(_artistColor);
        // Not a visual setting, so update the result without flashing a preview toast.
        _hotkey.ValueChanged += (_, _) =>
        {
            if (!_loading)
                Result.ShowHotkey = _hotkey.Value?.ToString() ?? "";
        };
        previewButton.Click += (_, _) => _preview(Result);
        resetButton.Click += (_, _) =>
        {
            LoadValues(new AppSettings());
            OnChanged();
        };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (DialogResult != DialogResult.OK)
            return;

        string? error = _validate(Result);
        if (error is not null)
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
            _hotkey.Focus();
        }
    }

    private void LoadValues(AppSettings s)
    {
        _loading = true;
        _width.Value = s.Width;
        _height.Value = s.Height;
        _marginX.Value = s.MarginX;
        _marginY.Value = s.MarginY;
        _duration.Value = (decimal)s.DurationSeconds;
        _corner.SelectedIndex = Math.Max(0, Array.FindIndex(Corners, c => c.Value == s.Corner));
        SetSwatch(_background, s.Background);
        SetSwatch(_titleColor, s.Title);
        SetSwatch(_artistColor, s.Artist);
        _hotkey.Value = Hotkey.TryParse(s.ShowHotkey, out var hk) ? hk : null;
        _loading = false;
    }

    private void OnChanged()
    {
        if (_loading)
            return;

        Result = new AppSettings
        {
            Width = (int)_width.Value,
            Height = (int)_height.Value,
            MarginX = (int)_marginX.Value,
            MarginY = (int)_marginY.Value,
            DurationSeconds = (double)_duration.Value,
            Corner = Corners[Math.Max(0, _corner.SelectedIndex)].Value,
            BackgroundColor = AppSettings.ToHex(_background.BackColor),
            TitleColor = AppSettings.ToHex(_titleColor.BackColor),
            ArtistColor = AppSettings.ToHex(_artistColor.BackColor),
            ShowHotkey = _hotkey.Value?.ToString() ?? "",
        };
        _preview(Result);
    }

    private void PickColor(Button target)
    {
        using var dlg = new ColorDialog { Color = target.BackColor, FullOpen = true };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        SetSwatch(target, dlg.Color);
        OnChanged();
    }

    private static void SetSwatch(Button b, Color c)
    {
        b.BackColor = c;
        b.Text = AppSettings.ToHex(c);
        // Keep the hex label readable on both light and dark swatches.
        b.ForeColor = (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 128 ? Color.Black : Color.White;
    }

    private static void AddRow(TableLayoutPanel grid, string label, Control input)
    {
        int row = grid.RowCount++;
        grid.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 16, 6),
        }, 0, row);
        input.Anchor = AnchorStyles.Left;
        grid.Controls.Add(input, 1, row);
    }

    private static NumericUpDown MakeNumber(int min, int max) =>
        new() { Minimum = min, Maximum = max, Width = 80 };

    private static Button MakeColorButton() =>
        new() { Width = 100, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
}
