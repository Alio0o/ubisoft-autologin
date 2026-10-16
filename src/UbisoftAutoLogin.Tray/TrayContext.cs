using System.Diagnostics;
using UbisoftAutoLogin.Core;

namespace UbisoftAutoLogin.Tray;

/// <summary>Tray icon + menu. No main window; the settings form opens only on request or first run.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly CredentialStore _store = new();
    private readonly AutoLoginEngine _engine;
    private readonly ToolStripMenuItem _enabledItem, _startupItem, _xboxItem, _statusItem;
    private SettingsForm? _settingsForm;

    public TrayContext(bool openSettingsOnStart)
    {
        _engine = new AutoLoginEngine(_settings, _store);

        _statusItem = new ToolStripMenuItem("Status: starting…") { Enabled = false };
        _enabledItem = new ToolStripMenuItem("Auto-login enabled", null, (_, _) => ToggleEnabled()) { Checked = _settings.AutoLoginEnabled };
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => SetStartup(!StartupManager.IsStartWithWindows())) { Checked = StartupManager.IsStartWithWindows() };
        _xboxItem = new ToolStripMenuItem("Start in Xbox mode", null, (_, _) => SetXboxMode(!StartupManager.IsXboxMode())) { Checked = StartupManager.IsXboxMode() };

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            _statusItem,
            new ToolStripSeparator(),
            _enabledItem,
            new ToolStripMenuItem("Configure Ubisoft credentials…", null, (_, _) => ShowSettings()),
            new ToolStripMenuItem("Test login detection", null, async (_, _) => await TestDetection()),
            new ToolStripMenuItem("View status", null, (_, _) => ShowStatus()),
            new ToolStripMenuItem("View logs", null, (_, _) => OpenLogs()),
            _startupItem,
            _xboxItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => ExitThread())
        });

        _icon = new NotifyIcon { Icon = TrayIcons.For(EngineStatus.Monitoring), Text = "Ubisoft Auto-Login", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => ShowSettings();

        _engine.StatusChanged += (s, d) =>
        {
            _statusItem.Text = "Status: " + Describe(s);
            _icon.Icon = TrayIcons.For(s);
            var tip = "Ubisoft Auto-Login – " + Describe(s);
            _icon.Text = tip.Length > 63 ? tip[..63] : tip;
        };
        // One balloon per problem; Windows suppresses these during full-screen games, which is what we want.
        _engine.ManualActionRequired += msg => _icon.ShowBalloonTip(8000, "Ubisoft Auto-Login", msg, ToolTipIcon.Warning);
        _engine.Start();

        if (openSettingsOnStart && !_store.Exists()) ShowSettings();
    }

    internal static string Describe(EngineStatus s) => s switch
    {
        EngineStatus.Disabled => "Auto-login off",
        EngineStatus.Monitoring => "Monitoring",
        EngineStatus.LauncherDetected => "Ubisoft Connect detected",
        EngineStatus.LoginInProgress => "Login in progress",
        EngineStatus.LoggedIn => "Logged in",
        EngineStatus.ManualActionRequired => "Manual action required",
        EngineStatus.Paused => "Paused (check password)",
        _ => s.ToString()
    };

    internal AppSettings Settings => _settings;
    internal CredentialStore Store => _store;
    internal AutoLoginEngine Engine => _engine;

    internal void ToggleEnabled() => SetEnabled(!_settings.AutoLoginEnabled);

    internal void SetEnabled(bool on)
    {
        _settings.AutoLoginEnabled = on;
        _settings.Save();
        _enabledItem.Checked = on;
        AppLog.Info("auto-login " + (on ? "enabled" : "disabled") + " by user");
        _engine.Resume();
    }

    internal void SetStartup(bool on)
    {
        StartupManager.SetStartWithWindows(on, Environment.ProcessPath!);
        _startupItem.Checked = StartupManager.IsStartWithWindows();
        AppLog.Info("start with Windows " + (on ? "on" : "off"));
    }

    internal void SetXboxMode(bool on)
    {
        try { StartupManager.SetXboxMode(on, Environment.ProcessPath!); }
        catch (Exception ex) { AppLog.Error("start in Xbox mode failed: " + ex.Message); }
        _xboxItem.Checked = StartupManager.IsXboxMode();
        AppLog.Info("start in Xbox mode " + (_xboxItem.Checked ? "on" : "off"));
    }

    internal async Task TestDetection()
    {
        var text = await AutoLoginEngine.TestDetectionAsync();
        AppLog.Info("test detection run");
        MessageBox.Show(text, "Ubisoft Auto-Login – Test detection", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowStatus() =>
        MessageBox.Show($"{Describe(_engine.Status)}\n\n{_engine.StatusDetail}\n\nCredentials saved: {(_store.Exists() ? "yes" : "no")}",
            "Ubisoft Auto-Login – Status", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private static void OpenLogs()
    {
        Directory.CreateDirectory(AppLog.Directory);
        if (!File.Exists(AppLog.FilePath)) File.WriteAllText(AppLog.FilePath, "");
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppLog.FilePath}\"") { UseShellExecute = false });
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false }) { _settingsForm.Activate(); return; }
        _settingsForm = new SettingsForm(this);
        _settingsForm.Show();
    }

    protected override void ExitThreadCore()
    {
        _engine.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        AppLog.Info("exited");
        base.ExitThreadCore();
    }
}
