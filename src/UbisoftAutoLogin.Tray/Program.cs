namespace UbisoftAutoLogin.Tray;

internal static class Program
{
    private const string MutexName = @"Local\UbisoftAutoLogin.Tray";
    /// <summary>Signalled by a newly opened instance to ask the running one to step aside.</summary>
    internal const string HandOverEventName = @"Local\UbisoftAutoLogin.HandOver";

    [STAThread]
    private static void Main(string[] args)
    {
        bool background = args.Contains("--background");
        bool startup = background || args.Contains("--tray");

        var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Started automatically while a copy already runs: nothing to do.
            if (startup) return;

            // Opened by the user (e.g. to change the password) while the invisible background copy runs:
            // ask it to exit, then continue with the normal tray app and settings window.
            using (var handOver = new EventWaitHandle(false, EventResetMode.AutoReset, HandOverEventName)) handOver.Set();
            try
            {
                if (!mutex.WaitOne(TimeSpan.FromSeconds(10))) return;
            }
            catch (AbandonedMutexException) { /* previous copy exited; we own it now */ }
        }

        using (mutex)
        {
            ApplicationConfiguration.Initialize();
            if (background) Application.Run(new BackgroundContext());
            else Application.Run(new TrayContext(openSettingsOnStart: !args.Contains("--tray")));
            mutex.ReleaseMutex();
        }
    }
}
