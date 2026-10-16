namespace UbisoftAutoLogin.Core;

public enum EngineStatus { Disabled, Monitoring, LauncherDetected, LoginInProgress, LoggedIn, ManualActionRequired, Paused }

/// <summary>
/// Watches for the verified Ubisoft Connect password prompt and fills it once.
/// Safety rules:
///  - only windows that pass <see cref="PromptDetector.IsVerifiedPrompt"/> are touched;
///  - only the plain password prompt with the e-mail pre-filled and an empty password field is filled;
///    2FA, CAPTCHA, recovery, errors and anything unrecognised are left to the user;
///  - one attempt per prompt window, and a pause if prompts keep coming back (<see cref="MaxAttemptsPerWindow"/> within <see cref="LoopWindow"/>);
///  - if the prompt is still there after submitting, auto-login pauses until credentials are re-saved
///    or auto-login is toggled, so a wrong password can never be submitted repeatedly.
/// </summary>
public sealed class AutoLoginEngine : IDisposable
{
    /// <summary>More than <see cref="MaxAttemptsPerWindow"/> attempts inside this window means a login loop.</summary>
    public static readonly TimeSpan LoopWindow = TimeSpan.FromMinutes(2);
    public const int MaxAttemptsPerWindow = 3;
    private static readonly TimeSpan SubmitTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PromptReadTimeout = TimeSpan.FromSeconds(30);

    private readonly WindowWatcher _watcher = new();
    private readonly CredentialStore _store;
    private readonly AppSettings _settings;
    private readonly SynchronizationContext? _ui;
    private readonly HashSet<IntPtr> _handled = new();
    private readonly Queue<DateTime> _attempts = new();
    private bool _paused;

    public EngineStatus Status { get; private set; } = EngineStatus.Disabled;
    public string StatusDetail { get; private set; } = "";

    /// <summary>Raised on the UI thread.</summary>
    public event Action<EngineStatus, string>? StatusChanged;
    /// <summary>Raised on the UI thread when the user has to act. Fired at most once per prompt.</summary>
    public event Action<string>? ManualActionRequired;

    public AutoLoginEngine(AppSettings settings, CredentialStore store)
    {
        _settings = settings;
        _store = store;
        _ui = SynchronizationContext.Current;
        _watcher.WindowEvent += OnWindowEvent;
    }

    public bool Paused => _paused;

    public void Start()
    {
        _watcher.Start();
        AppLog.Info("engine started; auto-login " + (_settings.AutoLoginEnabled ? "enabled" : "disabled"));
        RefreshIdleStatus();
        foreach (var w in PromptDetector.FindPrompts()) OnWindowEvent(w.Handle, "startup-scan");
    }

    /// <summary>Clears a pause caused by a failed attempt (e.g. after the user saves new credentials).</summary>
    public void Resume()
    {
        _paused = false;
        lock (_attempts) _attempts.Clear();
        RefreshIdleStatus();
    }

    public void RefreshIdleStatus()
    {
        if (!_settings.AutoLoginEnabled) SetStatus(EngineStatus.Disabled, "Auto-login is off");
        else if (_paused) SetStatus(EngineStatus.Paused, "Paused after a failed attempt. Re-save credentials or toggle auto-login.");
        else if (LauncherVerifier.LauncherProcesses().Any()) SetStatus(EngineStatus.LauncherDetected, "Ubisoft Connect detected; waiting for a password prompt");
        else SetStatus(EngineStatus.Monitoring, "Monitoring; Ubisoft Connect not running");
    }

    private void OnWindowEvent(IntPtr hwnd, string kind)
    {
        uint pid = Native.Pid(hwnd);
        if (!LauncherVerifier.IsLauncherPid(pid)) return; // ignore every other application
        if (Status is EngineStatus.Monitoring) RefreshIdleStatus();

        if (!string.Equals(Native.ClassName(hwnd), PromptDetector.PromptWindowClass, StringComparison.Ordinal)) return;
        if (!PromptDetector.IsVerifiedPrompt(hwnd, out var id))
        {
            AppLog.Warn($"prompt-like window ignored: verification failed ({(id is null ? "not visible/owned" : string.Join("; ", id.Failures()))})");
            return;
        }
        if (!_handled.Add(hwnd)) return;
        _handled.RemoveWhere(h => !Native.IsWindow(h));
        AppLog.Info($"verified Ubisoft Connect password prompt detected ({kind})");

        if (!_settings.AutoLoginEnabled) { AppLog.Info("auto-login disabled; leaving prompt to the user"); return; }
        if (_paused) { Manual("Auto-login is paused after a failed attempt. Enter the password manually."); return; }
        // A successful login closes the prompt, and relaunching a game legitimately shows it again, so a new
        // prompt after success is normal. Only a burst of prompts (a real loop) pauses auto-login.
        lock (_attempts)
        {
            while (_attempts.Count > 0 && DateTime.Now - _attempts.Peek() > LoopWindow) _attempts.Dequeue();
            if (_attempts.Count >= MaxAttemptsPerWindow)
            {
                Pause($"Ubisoft asked for the password {MaxAttemptsPerWindow} times within {LoopWindow.TotalMinutes:0} minutes.");
                return;
            }
        }

        _ = Task.Run(() => HandlePromptAsync(hwnd));
    }

    private async Task HandlePromptAsync(IntPtr hwnd)
    {
        try
        {
            SetStatus(EngineStatus.LoginInProgress, "Reading the prompt");
            Classification? c = null;
            // Right after boot Ubisoft Connect can show the window well before it draws its content,
            // so keep reading until the screen is recognisable (or the timeout passes).
            var readUntil = DateTime.Now + PromptReadTimeout;
            int reads = 0;
            do
            {
                await Task.Delay(reads == 0 ? 700 : 750);
                if (!PromptDetector.IsVerifiedPrompt(hwnd, out _)) { AppLog.Info("prompt closed before it could be read"); RefreshIdleStatus(); return; }
                c = await PromptClassifier.ClassifyAsync(hwnd);
                reads++;
            } while (c.Kind == PromptKind.Unknown && DateTime.Now < readUntil);
            if (reads > 1) AppLog.Info($"prompt read {reads} times before it was recognisable");
            AppLog.Info("prompt classified: " + c!.Reason);

            if (!c.SafeToAutoFill)
            {
                Manual(c.Kind switch
                {
                    PromptKind.TwoFactor => "Ubisoft asks for a two-factor code. Please enter it manually.",
                    PromptKind.Captcha => "Ubisoft shows a CAPTCHA. Please complete it manually.",
                    PromptKind.AccountRecovery => "Ubisoft shows an account recovery screen. Manual action needed.",
                    PromptKind.NetworkError => "Ubisoft Connect reports a connection problem.",
                    PromptKind.PasswordLogin when !c.EmailPrefilled => "The e-mail field is empty; auto-login only fills a pre-filled prompt.",
                    PromptKind.PasswordLogin => "The password field is not empty; leaving it to you.",
                    _ => "Unrecognised Ubisoft Connect screen; leaving it to you."
                }, notify: c.Kind is PromptKind.TwoFactor or PromptKind.Captcha or PromptKind.AccountRecovery);
                return;
            }

            if (!_store.Exists()) { Manual("No Ubisoft credentials saved. Open the tray icon > Configure credentials."); return; }

            bool Valid(IntPtr h) => PromptDetector.IsVerifiedPrompt(h, out _);
            if (!SecureTyper.EnsureForeground(hwnd, _settings.BringPromptToFront))
            {
                Manual("Could not bring the Ubisoft prompt to the front safely; nothing was typed.");
                return;
            }

            lock (_attempts) _attempts.Enqueue(DateTime.Now);
            AppLog.Info("authentication attempt initiated");
            var pwd = _store.ReadPassword();
            bool typed;
            try { typed = pwd is not null && SecureTyper.TypeInto(hwnd, Valid, pwd); }
            finally { if (pwd is not null) Array.Clear(pwd); }

            if (!typed || !SecureTyper.PressEnter(hwnd, Valid))
            {
                AppLog.Warn("typing aborted: prompt lost focus or failed verification");
                Manual("Auto-login stopped because the Ubisoft prompt lost focus. Finish the login manually.");
                return;
            }

            var deadline = DateTime.Now + SubmitTimeout;
            while (DateTime.Now < deadline)
            {
                await Task.Delay(500);
                if (!Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd))
                {
                    AppLog.Info("authentication succeeded: prompt closed");
                    SetStatus(EngineStatus.LoggedIn, "Logged in automatically at " + DateTime.Now.ToString("HH:mm"));
                    return;
                }
            }

            var after = await PromptClassifier.ClassifyAsync(hwnd);
            AppLog.Warn("prompt still open after submit: " + after.Reason);
            if (after.Kind == PromptKind.TwoFactor) Manual("Ubisoft now asks for a two-factor code. Please enter it manually.");
            else Pause("Ubisoft did not accept the login; the saved password may be wrong.");
        }
        catch (Exception ex)
        {
            AppLog.Error("auto-login failed: " + ex.GetType().Name + ": " + ex.Message);
            Manual("Auto-login hit an error; please log in manually. See the log for details.");
        }
    }

    private void Pause(string why)
    {
        _paused = true;
        AppLog.Warn("auto-login paused: " + why);
        Post(() =>
        {
            SetStatus(EngineStatus.Paused, why);
            if (_settings.NotifyOnManualAction) ManualActionRequired?.Invoke(why + " Auto-login is paused.");
        });
    }

    /// <summary>
    /// Records that the user has to act. A notification is shown only when it is certain the user must do
    /// something specific (2FA, CAPTCHA, no credentials, ...); unclear cases are logged without interrupting.
    /// </summary>
    private void Manual(string why, bool notify = true)
    {
        AppLog.Warn("manual action required: " + why);
        Post(() =>
        {
            SetStatus(EngineStatus.ManualActionRequired, why);
            if (notify && _settings.NotifyOnManualAction) ManualActionRequired?.Invoke(why);
        });
    }

    private void SetStatus(EngineStatus s, string detail) => Post(() =>
    {
        if (Status == s && StatusDetail == detail) return;
        Status = s; StatusDetail = detail;
        StatusChanged?.Invoke(s, detail);
    });

    private void Post(Action a)
    {
        if (_ui is null || SynchronizationContext.Current == _ui) a();
        else _ui.Post(_ => a(), null);
    }

    /// <summary>Test Login Detection: describes any visible prompt without touching it.</summary>
    public static async Task<string> TestDetectionAsync()
    {
        var lines = new List<string>();
        var procs = LauncherVerifier.LauncherProcesses().ToList();
        if (procs.Count == 0) lines.Add("Ubisoft Connect is not running.");
        foreach (var p in procs)
        {
            var id = LauncherVerifier.Identify((uint)p.Id);
            lines.Add($"Ubisoft Connect (pid {p.Id}): {(id.IsTrusted ? "verified" : "NOT verified: " + string.Join("; ", id.Failures()))}" +
                      (id.UipiBlocked ? " — running elevated, run this tool as admin too" : ""));
        }
        var prompts = PromptDetector.FindPrompts();
        if (prompts.Count == 0) lines.Add("No password prompt is open right now. Launch the game, then test again.");
        foreach (var w in prompts)
        {
            var c = await PromptClassifier.ClassifyAsync(w.Handle);
            lines.Add($"Prompt found: {c.Kind}. {(c.SafeToAutoFill ? "Auto-login would fill it." : "Auto-login would leave it to you.")} ({c.Reason})");
        }
        return string.Join(Environment.NewLine, lines);
    }

    public void Dispose() => _watcher.Dispose();
}
