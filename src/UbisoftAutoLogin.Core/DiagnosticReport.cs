using System.Text;
using System.Windows.Automation;

namespace UbisoftAutoLogin.Core;

/// <summary>Builds the sanitized technical report users can export and share.</summary>
public static class DiagnosticReport
{
    public static string Build(IEnumerable<IntPtr>? extraWindows = null, bool includeHidden = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Ubisoft Connect diagnostic report");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"OS: {Environment.OSVersion.VersionString}");
        sb.AppendLine("Sanitized: no field values are read; password field names, e-mails and token-like strings are redacted.");
        sb.AppendLine();

        var install = LauncherVerifier.InstallDir();
        sb.AppendLine($"Install dir registered: {(install is null ? "NOT FOUND" : "yes")}");
        if (install is not null) Version(sb, Path.Combine(install, "upc.exe"));
        sb.AppendLine();

        sb.AppendLine("## Processes");
        foreach (var p in LauncherVerifier.LauncherProcesses())
            sb.AppendLine(Describe(LauncherVerifier.Identify((uint)p.Id)));
        foreach (var name in LauncherVerifier.HelperExeNames)
            sb.AppendLine($"{name}: {System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name)).Length} running");
        sb.AppendLine();

        var windows = WindowInfo.LauncherWindows(!includeHidden).Select(w => w.Handle).ToList();
        if (extraWindows is not null) windows.AddRange(extraWindows.Where(h => !windows.Contains(h)));

        sb.AppendLine($"## Windows ({windows.Count})");
        foreach (var h in windows)
        {
            if (!Native.IsWindow(h)) continue;
            var w = WindowInfo.From(h);
            sb.AppendLine().AppendLine("### " + w);
            var id = LauncherVerifier.Identify(w.Pid);
            sb.AppendLine("identity: " + Describe(id));
            // Launcher windows, plus windows of other Ubisoft-signed executables (games), get their tree captured.
            if (!id.IsTrusted && !(id.SignatureValid && id.SignerIsUbisoft))
            {
                sb.AppendLine("(not a Ubisoft-signed process: UI tree not captured)");
                continue;
            }
            try
            {
                var probe = PromptProbe.Probe(h, requireLauncher: false);
                sb.AppendLine($"probe: kind={probe.Kind} passwordFields={probe.PasswordFields} edits={probe.EditFields} buttons=[{string.Join(" | ", probe.ButtonNames)}] {probe.Note}");
                var nodes = UiaInspector.Walk(AutomationElement.FromHandle(h));
                sb.AppendLine($"UIA nodes: {nodes.Count}");
                sb.AppendLine("```");
                sb.Append(UiaInspector.Format(nodes));
                sb.AppendLine("```");
            }
            catch (Exception ex) { sb.AppendLine("UIA error: " + ex.GetType().Name + ": " + UiaInspector.Sanitize(ex.Message, 200)); }
        }
        return sb.ToString();
    }

    private static void Version(StringBuilder sb, string path)
    {
        if (!File.Exists(path)) return;
        var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
        sb.AppendLine($"upc.exe version: {v.ProductVersion}");
    }

    private static string Describe(ProcessIdentity i) =>
        $"pid={i.Pid} {i.ProcessName} file={Path.GetFileName(i.ImagePath ?? "?")} underInstallDir={i.PathUnderInstallDir} " +
        $"sigValid={i.SignatureValid} signerUbisoft={i.SignerIsUbisoft} sameSession={i.SameSession} elevated={Fmt(i.Elevated)} " +
        $"selfElevated={Fmt(i.SelfElevated)} trusted={i.IsTrusted}";

    private static string Fmt(bool? b) => b is null ? "unknown(access denied)" : b.Value.ToString();
}
