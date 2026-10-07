using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpotifyToast;

public enum ToastCorner
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft,
}

/// <summary>User-customizable toast appearance, persisted as JSON in %APPDATA%\SpotifyToast.</summary>
public sealed class AppSettings
{
    public const int MinWidth = 200, MaxWidth = 800;
    public const int MinHeight = 60, MaxHeight = 300;
    public const int MaxMargin = 400;
    public const double MinDuration = 1, MaxDuration = 60;

    // Sizes are in logical pixels (96 DPI) and scaled to the monitor's DPI when shown.
    public int Width { get; set; } = 340;
    public int Height { get; set; } = 76;
    public ToastCorner Corner { get; set; } = ToastCorner.BottomRight;
    public int MarginX { get; set; } = 16;
    public int MarginY { get; set; } = 16;

    /// <summary>How long the toast stays fully visible, excluding the fade in/out.</summary>
    public double DurationSeconds { get; set; } = 4;

    /// <summary>
    /// Global shortcut that shows the current song, e.g. "Ctrl+Alt+Shift+S".
    /// Empty means disabled. The default uses three modifiers to avoid clashing with
    /// other apps and with AltGr (= Ctrl+Alt) characters on non-US keyboard layouts.
    /// </summary>
    public string ShowHotkey { get; set; } = DefaultHotkey;
    public const string DefaultHotkey = "Ctrl+Alt+Shift+S";

    // Stored as "#RRGGBB" so the JSON file stays human-editable.
    public string BackgroundColor { get; set; } = "#181818";
    public string TitleColor { get; set; } = "#FFFFFF";
    public string ArtistColor { get; set; } = "#1ED760";

    [JsonIgnore] public Color Background => ParseColor(BackgroundColor, Color.FromArgb(24, 24, 24));
    [JsonIgnore] public Color Title => ParseColor(TitleColor, Color.White);
    [JsonIgnore] public Color Artist => ParseColor(ArtistColor, Color.FromArgb(30, 215, 96));

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpotifyToast", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Loads settings, falling back to defaults if the file is missing or corrupt.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return Normalize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.WriteLine($"AppSettings: failed to load, using defaults: {ex.Message}");
        }
        return new AppSettings();
    }

    /// <exception cref="IOException">Thrown when the file can't be written; callers show the error.</exception>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // Write to a temp file first so a crash mid-write can't corrupt existing settings.
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, FilePath, overwrite: true);
    }

    /// <summary>Clamps values from a hand-edited file into the supported range.</summary>
    private static AppSettings Normalize(AppSettings? s)
    {
        if (s is null)
            return new AppSettings();

        s.Width = Math.Clamp(s.Width, MinWidth, MaxWidth);
        s.Height = Math.Clamp(s.Height, MinHeight, MaxHeight);
        s.MarginX = Math.Clamp(s.MarginX, 0, MaxMargin);
        s.MarginY = Math.Clamp(s.MarginY, 0, MaxMargin);
        s.DurationSeconds = double.IsFinite(s.DurationSeconds)
            ? Math.Clamp(s.DurationSeconds, MinDuration, MaxDuration)
            : 4;
        if (string.IsNullOrWhiteSpace(s.ShowHotkey))
            s.ShowHotkey = "";
        else
            s.ShowHotkey = Hotkey.TryParse(s.ShowHotkey, out var hk) ? hk.ToString() : DefaultHotkey;
        if (!Enum.IsDefined(s.Corner))
            s.Corner = ToastCorner.BottomRight;
        return s;
    }

    private static Color ParseColor(string? hex, Color fallback)
    {
        if (hex is { Length: 7 } && hex[0] == '#' &&
            int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out int rgb))
        {
            return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }
        return fallback;
    }
}
