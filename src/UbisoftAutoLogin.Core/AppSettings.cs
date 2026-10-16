using System.Text.Json;
using Microsoft.Win32;

namespace UbisoftAutoLogin.Core;

/// <summary>Non-secret settings, stored as JSON next to the log. Credentials never go here.</summary>
public sealed class AppSettings
{
    public bool AutoLoginEnabled { get; set; } = true;

    /// <summary>
    /// If the verified prompt is not already the foreground window, bring it to the front before typing.
    /// Keys are only ever sent while the verified prompt is the foreground window.
    /// </summary>
    public bool BringPromptToFront { get; set; } = true;

    /// <summary>Show a tray notification when manual action is needed.</summary>
    public bool NotifyOnManualAction { get; set; } = true;

    private static string FilePath => Path.Combine(AppLog.Directory, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception ex) { AppLog.Warn("settings unreadable, using defaults: " + ex.GetType().Name); }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(AppLog.Directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}

/// <summary>
/// Two independent, opt-in ways to start automatically (both off by default; running both is harmless
/// because the app is single-instance):
///  - Start with Windows: the per-user Run key, started by the Explorer desktop.
///  - Start in Xbox mode: a per-user Task Scheduler sign-in task. Xbox mode / full screen experience shells
///    (and tools such as AnyFSE) don't start Explorer until you exit to the desktop, so Run-key apps don't run
///    there; Task Scheduler is a system service and runs whichever shell is used. No admin rights needed.
/// </summary>
public static class StartupManager
{
    private const string Name = "UbisoftAutoLogin";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsStartWithWindows()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(Name) is string;
    }

    public static void SetStartWithWindows(bool enabled, string exePath)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) k.SetValue(Name, $"\"{exePath}\" --tray");
        else k.DeleteValue(Name, throwOnMissingValue: false);
    }

    public static bool IsXboxMode() => SchTasks($"/Query /TN \"{Name}\"") == 0;

    public static void SetXboxMode(bool enabled, string exePath)
    {
        if (!enabled)
        {
            if (IsXboxMode()) SchTasks($"/Delete /TN \"{Name}\" /F");
            return;
        }
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts Ubisoft Auto-Login at sign-in, including in Xbox mode / full screen experience.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{Escape(user)}</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
                <Hidden>true</Hidden>
                <StartWhenAvailable>true</StartWhenAvailable>
              </Settings>
              <Actions Context="Author"><Exec><Command>{Escape(exePath)}</Command><Arguments>--background</Arguments></Exec></Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), $"UbisoftAutoLogin-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
            int code = SchTasks($"/Create /TN \"{Name}\" /XML \"{file}\" /F");
            if (code != 0) throw new InvalidOperationException("schtasks /Create failed with exit code " + code);
        }
        finally { File.Delete(file); }
    }

    private static string Escape(string s) => System.Security.SecurityElement.Escape(s) ?? s;

    private static int SchTasks(string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", args)
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        return p.ExitCode;
    }
}
