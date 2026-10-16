using System.Diagnostics;
using UbisoftAutoLogin.Core;

namespace UbisoftAutoLogin.Diagnostics;

/// <summary>
/// Windowless tracer for "who stole focus / what popped up" problems. Logs, with timestamps:
/// every foreground change, every top-level window that is shown, and every process that starts.
/// Window titles are redacted (e-mails/tokens) and cut short; nothing else is recorded.
/// </summary>
internal sealed class FocusTrace : ApplicationContext
{
    private readonly WindowWatcher _watcher = new();
    private readonly string _log;
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _stop = new();
    private HashSet<int> _known;

    public FocusTrace(string dir, TimeSpan duration)
    {
        Directory.CreateDirectory(dir);
        _log = Path.Combine(dir, $"focus-trace-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        Log($"tracing for {duration.TotalMinutes:0} min; boot at {Boot():HH:mm:ss}");
        Log("foreground at start: " + Describe(WindowInfoFg()));

        _known = Process.GetProcesses().Select(p => p.Id).ToHashSet();
        Log("processes at start: " + string.Join(", ", Process.GetProcesses().Select(p => p.ProcessName).Distinct().OrderBy(n => n)));

        _watcher.WindowEvent += (h, kind) =>
        {
            if (!WindowInfo.Exists(h)) return;
            var w = WindowInfo.From(h);
            if (kind == "show" && !w.Visible) return;
            Log($"{(kind == "foreground" ? "FOCUS" : "show ")} {Describe(w)}");
        };
        _watcher.Start();

        _poll.Tick += (_, _) => PollProcesses();
        _poll.Start();
        _stop.Interval = (int)duration.TotalMilliseconds;
        _stop.Tick += (_, _) => { Log("done"); _watcher.Dispose(); ExitThread(); };
        _stop.Start();
    }

    private static WindowInfo? WindowInfoFg()
    {
        var h = ForegroundHandle();
        return h == IntPtr.Zero ? null : WindowInfo.From(h);
    }

    private static IntPtr ForegroundHandle() => GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    private static string Describe(WindowInfo? w) => w is null ? "(none)" :
        $"proc={w.ProcessName}({w.Pid}) class='{w.ClassName}' title='{Redact.Sanitize(w.Title, 60)}' rect={w.Left},{w.Top} {w.Width}x{w.Height}";

    private void PollProcesses()
    {
        var now = Process.GetProcesses();
        foreach (var p in now)
        {
            if (_known.Contains(p.Id)) continue;
            string parent = "";
            try { parent = ParentName(p.Id); } catch { }
            Log($"START {p.ProcessName}({p.Id}) parent={parent}");
        }
        _known = now.Select(p => p.Id).ToHashSet();
    }

    private static string ParentName(int pid)
    {
        using var q = new System.Management.ManagementObjectSearcher($"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId={pid}");
        foreach (var o in q.Get())
        {
            int ppid = Convert.ToInt32(o["ParentProcessId"]);
            try { using var pp = Process.GetProcessById(ppid); return $"{pp.ProcessName}({ppid})"; } catch { return $"?({ppid})"; }
        }
        return "?";
    }

    private static DateTime Boot() => DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);

    private void Log(string s) => File.AppendAllText(_log, $"[{DateTime.Now:HH:mm:ss.fff}] {s}{Environment.NewLine}");
}
