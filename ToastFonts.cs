using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace SpotifyToast;

/// <summary>
/// Loads the bundled Google Sans fonts so the toast looks the same on every PC, whether or
/// not the font is installed. Falls back to Segoe UI if the embedded fonts can't be loaded.
/// </summary>
/// <remarks>
/// Each font is registered twice: with a <see cref="PrivateFontCollection"/> so GDI+ can
/// create a <see cref="Font"/> for it, and with <c>AddFontMemResourceEx</c> so GDI (which
/// <see cref="TextRenderer"/> uses) can render it. Both registrations are private to this
/// process, and the font memory is kept for the app's lifetime.
/// </remarks>
internal static class ToastFonts
{
    private const string RegularResource = "SpotifyToast.GoogleSans-Regular.ttf";
    private const string MediumResource = "SpotifyToast.GoogleSans-Medium.ttf";

    private static readonly PrivateFontCollection Collection = new();
    private static readonly FontFamily? Regular;
    private static readonly FontFamily? Medium;

    static ToastFonts()
    {
        try
        {
            AddEmbeddedFont(RegularResource);
            AddEmbeddedFont(MediumResource);
            Regular = Find("Google Sans");
            Medium = Find("Google Sans Medium");
        }
        catch (Exception ex) when (ex is IOException or ExternalException or ArgumentException)
        {
            Regular = Medium = null; // use the Segoe UI fallback
        }
    }

    /// <summary>Font for the song title.</summary>
    public static Font Title(float px) =>
        Medium is not null
            ? new Font(Medium, px, GraphicsUnit.Pixel)
            : new Font("Segoe UI Semibold", px, GraphicsUnit.Pixel);

    /// <summary>Font for the artist name.</summary>
    public static Font Artist(float px) =>
        Regular is not null
            ? new Font(Regular, px, GraphicsUnit.Pixel)
            : new Font("Segoe UI", px, GraphicsUnit.Pixel);

    private static void AddEmbeddedFont(string resource)
    {
        using var stream = typeof(ToastFonts).Assembly.GetManifestResourceStream(resource)
            ?? throw new IOException($"Missing embedded font {resource}.");
        byte[] data = new byte[stream.Length];
        stream.ReadExactly(data);

        // Never freed: the PrivateFontCollection reads from this memory while the font is in use.
        IntPtr memory = Marshal.AllocCoTaskMem(data.Length);
        Marshal.Copy(data, 0, memory, data.Length);
        Collection.AddMemoryFont(memory, data.Length);

        if (AddFontMemResourceEx(memory, (uint)data.Length, IntPtr.Zero, out _) == IntPtr.Zero)
            throw new ExternalException($"Windows could not load the font {resource}.");
    }

    private static FontFamily? Find(string name) =>
        Array.Find(Collection.Families, f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    [DllImport("gdi32.dll")]
    private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, out uint pcFonts);
}
