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
