using UbisoftAutoLogin.Core;

namespace UbisoftAutoLogin.Diagnostics;

/// <summary>
/// Phase-1 diagnostic tool. Watches for windows (event driven), captures sanitized UI Automation
/// trees of Ubisoft-signed windows, and can locate + focus a password field. It never types anything.
/// </summary>
internal sealed class DiagnosticsForm : Form
{
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f) };
    private readonly ListBox _windows = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly CheckBox _watch = new() { Text = "Watch && auto-capture", AutoSize = true };
    private readonly CheckBox _hidden = new() { Text = "Include hidden windows", AutoSize = true };
    private readonly WindowWatcher _watcher = new();
    private readonly HashSet<IntPtr> _captured = new();
    private readonly HashSet<IntPtr> _seen = new();
    private readonly List<IntPtr> _extra = new();
    private string _lastReport = "";

    public DiagnosticsForm()
    {
        Text = "Ubisoft Auto-Login – Diagnostics";
        Width = 1100; Height = 750;

        var inspect = new Button { Text = "Inspect now", AutoSize = true };
        var focus = new Button { Text = "Locate && focus password field (types nothing)", AutoSize = true };
        var export = new Button { Text = "Export report…", AutoSize = true };
        inspect.Click += (_, _) => Inspect();
        focus.Click += (_, _) => FocusSelected();
        export.Click += (_, _) => Export();
        _watch.CheckedChanged += (_, _) => { if (_watch.Checked) { _watcher.Start(); Log("Watching for windows…"); } else { _watcher.Stop(); Log("Watch stopped."); } };
        _watcher.WindowEvent += OnWindowEvent;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        bar.Controls.AddRange(new Control[] { inspect, _watch, _hidden, focus, export });

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 140 };
        split.Panel1.Controls.Add(_windows);
        split.Panel2.Controls.Add(_log);

        Controls.Add(split);
        Controls.Add(bar);
        Load += (_, _) => { RefreshWindows(); Log("Open the Ubisoft password prompt (launch the game from Steam), then press Inspect now — or tick Watch first."); };
        FormClosed += (_, _) => _watcher.Dispose();
    }

    private void Log(string s) => _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}{Environment.NewLine}");

    private void RefreshWindows()
    {
        _windows.Items.Clear();
        foreach (var w in WindowInfo.LauncherWindows(!_hidden.Checked)) _windows.Items.Add(new Item(w.Handle, w.ToString()));
        foreach (var h in _extra.Where(WindowInfo.Exists)) _windows.Items.Add(new Item(h, "[game] " + WindowInfo.From(h)));
    }

    private void Inspect()
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            _lastReport = DiagnosticReport.Build(_extra.Where(WindowInfo.Exists), _hidden.Checked);
            _log.Text = _lastReport;
            RefreshWindows();
        }
        finally { Cursor = Cursors.Default; }
    }

    private async void OnWindowEvent(IntPtr hwnd, string kind)
    {
        if (!_seen.Add(hwnd) && kind == "show") return;
        var w = WindowInfo.From(hwnd);
        if (!w.Visible) return;
        var id = LauncherVerifier.Identify(w.Pid);
        bool launcher = id.IsTrusted && LauncherVerifier.IsLauncherPid(w.Pid);
        bool ubiSigned = id.SignatureValid && id.SignerIsUbisoft;
        // Unrelated apps: process + class only, never their titles.
        Log(ubiSigned ? $"{kind}: {w} launcher={launcher}" : $"{kind}: other app proc={w.ProcessName} class='{w.ClassName}'");
        if (!ubiSigned || _captured.Contains(hwnd)) return;

        if (!launcher && !_extra.Contains(hwnd)) _extra.Add(hwnd);
        _captured.Add(hwnd);
        await Task.Delay(2000); // let the embedded browser finish rendering
        if (!WindowInfo.Exists(hwnd)) { Log("window closed before capture"); return; }
        Log("auto-capturing…");
        Inspect();
        var file = SaveReport("auto");
        Log("Saved " + file);
        _captured.Remove(hwnd); // allow recapture next time it is shown
    }

    private void FocusSelected()
    {
        if (_windows.SelectedItem is not Item it) { Log("Select a window in the list first."); return; }
        Log(PromptProbe.FocusPasswordField(it.Handle));
    }

    private string SaveReport(string tag)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "UbisoftAutoLogin");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"diag-{tag}-{DateTime.Now:yyyyMMdd-HHmmss}.md");
        File.WriteAllText(file, _lastReport);
        return file;
    }

    private void Export()
    {
        if (_lastReport.Length == 0) Inspect();
        using var dlg = new SaveFileDialog { Filter = "Markdown|*.md|Text|*.txt", FileName = $"ubisoft-diag-{DateTime.Now:yyyyMMdd-HHmmss}.md" };
        if (dlg.ShowDialog(this) == DialogResult.OK) { File.WriteAllText(dlg.FileName, _lastReport); Log("Exported " + dlg.FileName); }
    }

    private sealed record Item(IntPtr Handle, string Text) { public override string ToString() => Text; }
}
