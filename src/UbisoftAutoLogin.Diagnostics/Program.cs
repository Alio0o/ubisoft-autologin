using UbisoftAutoLogin.Core;

namespace UbisoftAutoLogin.Diagnostics;

internal static class Program
{
    /// <summary>
    /// UbisoftAutoLoginDiagnostics.exe                      → interactive window
    /// UbisoftAutoLoginDiagnostics.exe --report out.md       → one-shot sanitized report (visible launcher windows)
    /// UbisoftAutoLoginDiagnostics.exe --report out.md --all → include hidden launcher windows
    /// UbisoftAutoLoginDiagnostics.exe --watch outDir [min]  → windowless watch, auto-captures reports
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        int i = Array.IndexOf(args, "--report");
        if (i >= 0 && i + 1 < args.Length)
        {
            File.WriteAllText(args[i + 1], DiagnosticReport.Build(includeHidden: args.Contains("--all")));
            return 0;
        }

        int c = Array.IndexOf(args, "--classify");
        if (c >= 0 && c + 1 < args.Length)
        {
            // --classify <outFile>: classification of each visible verified prompt (no text, no image saved)
            var lines = new List<string>();
            foreach (var pw in PromptDetector.FindPrompts())
            {
                var r = PromptClassifier.ClassifyAsync(pw.Handle).GetAwaiter().GetResult();
                lines.Add($"{pw} -> {r.Kind} safeToAutoFill={r.SafeToAutoFill} ({r.Reason})");
                if (args.Contains("--verbose"))
                    foreach (var t in PromptClassifier.OcrWindowAsync(pw.Handle).GetAwaiter().GetResult() ?? new()) lines.Add("  ocr: " + UiaInspector.Sanitize(t));
            }
            if (lines.Count == 0) lines.Add("no verified prompt visible");
            File.WriteAllLines(args[c + 1], lines);
            return 0;
        }

        ApplicationConfiguration.Initialize();

        int w = Array.IndexOf(args, "--watch");
        if (w >= 0 && w + 1 < args.Length)
        {
            // --watch <outDir> [minutes]
            int minutes = w + 2 < args.Length && int.TryParse(args[w + 2], out var m) ? m : 15;
            Application.Run(new HeadlessWatch(args[w + 1], TimeSpan.FromMinutes(minutes)));
            return 0;
        }

        Application.Run(new DiagnosticsForm());
        return 0;
    }
}
