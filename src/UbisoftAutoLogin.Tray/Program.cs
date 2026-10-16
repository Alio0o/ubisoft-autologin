namespace UbisoftAutoLogin.Tray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\UbisoftAutoLogin.Tray", out bool first);
        if (!first) return; // already running in the tray

        ApplicationConfiguration.Initialize();
        if (args.Contains("--background")) Application.Run(new BackgroundContext());
        else Application.Run(new TrayContext(openSettingsOnStart: !args.Contains("--tray")));
    }
}
