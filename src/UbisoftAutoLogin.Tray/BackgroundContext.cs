using UbisoftAutoLogin.Core;

namespace UbisoftAutoLogin.Tray;

/// <summary>
/// Windowless mode used by "Start in Xbox mode": no tray icon, no notifications, no windows at all, so
/// nothing can take focus away from Steam Big Picture or make the full screen experience offer to switch
/// to the desktop. Everything is written to the log instead.
/// </summary>
internal sealed class BackgroundContext : ApplicationContext
{
    private readonly AutoLoginEngine _engine;

    public BackgroundContext()
    {
        var settings = AppSettings.Load();
        settings.NotifyOnManualAction = false;
        _engine = new AutoLoginEngine(settings, new CredentialStore());
        _engine.Start();
        AppLog.Info("running in background mode (no tray icon)");
    }

    protected override void ExitThreadCore()
    {
        _engine.Dispose();
        base.ExitThreadCore();
    }
}
