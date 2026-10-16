using UbisoftAutoLogin.Core;

namespace UbisoftAutoLogin.Diagnostics;

/// <summary>
/// Windowless watch mode for use while gaming: no UI, no focus changes. Logs window events and
/// saves a sanitized report each time a visible Ubisoft-signed window appears.
/// </summary>
internal sealed class HeadlessWatch : ApplicationContext
{
    private readonly WindowWatcher _watcher = new();
    private readonly string _dir;
    private readonly string _log;
    private readonly Dictionary<IntPtr, DateTime> _lastCapture = new();
    private readonly System.Windows.Forms.Timer _stop = new();

    public HeadlessWatch(string dir, TimeSpan duration)
    {
        _dir = dir;
        Directory.CreateDirectory(dir);
        _log = Path.Combine(dir, $"watch-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        Log($"watching for {duration.TotalMinutes:0} min");
        Log("initial launcher windows: " + string.Join(" ; ", WindowInfo.LauncherWindows(true).Select(w => w.ToString())));
        _watcher.WindowEvent += OnWindowEvent;
        _watcher.Start();
        _stop.Interval = (int)duration.TotalMilliseconds;
        _stop.Tick += (_, _) => { Log("done"); _watcher.Dispose(); ExitThread(); };
        _stop.Start();
    }

    private void Log(string s) => File.AppendAllText(_log, $"[{DateTime.Now:HH:mm:ss.fff}] {s}{Environment.NewLine}");

    private async void OnWindowEvent(IntPtr hwnd, string kind)
    {
        try
        {
            var w = WindowInfo.From(hwnd);
            if (!w.Visible) return;
            var id = LauncherVerifier.Identify(w.Pid);
            bool ubiSigned = id.SignatureValid && id.SignerIsUbisoft;
            Log(ubiSigned ? $"{kind}: {w} launcher={id.IsTrusted}" : $"{kind}: other app proc={w.ProcessName} class='{w.ClassName}'");
            if (!ubiSigned) return;
            if (_lastCapture.TryGetValue(hwnd, out var t) && DateTime.Now - t < TimeSpan.FromSeconds(10)) return;
            _lastCapture[hwnd] = DateTime.Now;

            foreach (var delay in new[] { 1500, 5000 }) // capture twice: early render and settled state
            {
                await Task.Delay(delay);
                if (!WindowInfo.Exists(hwnd)) { Log("window closed before capture"); return; }
                var extra = id.IsTrusted ? Array.Empty<IntPtr>() : new[] { hwnd };
                var file = Path.Combine(_dir, $"diag-{DateTime.Now:yyyyMMdd-HHmmss-fff}.md");
                File.WriteAllText(file, DiagnosticReport.Build(extra));
                Log("captured " + Path.GetFileName(file));
            }
        }
        catch (Exception ex) { Log("error: " + ex.GetType().Name + " " + UiaInspector.Sanitize(ex.Message, 200)); }
    }
}
