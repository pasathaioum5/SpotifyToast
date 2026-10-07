namespace SpotifyToast;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Only allow one instance per user session.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\SpotifyToast.SingleInstance", out bool isNew);
        if (!isNew)
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}
