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

/// <summary>"Start with Windows" via the per-user Run key. Off unless the user turns it on.</summary>
/// <remarks>
/// Uses a per-user Task Scheduler task with a logon trigger instead of the Run key. Run-key apps are started
/// by the Explorer desktop shell, which Xbox mode / full-screen-experience shells (and tools such as AnyFSE)
/// don't start until you exit to the desktop. Task Scheduler is a system service, so the task runs at sign-in
/// whichever shell is used. Creating a task for your own account needs no admin rights.
/// </remarks>
public static class StartupManager
{
    private const string TaskName = "UbisoftAutoLogin";
    private const string LegacyRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled() => SchTasks($"/Query /TN \"{TaskName}\"") == 0;

    /// <summary>Converts a Run-key entry from an older version into the scheduled task.</summary>
    public static void MigrateLegacy(string exePath)
    {
        using var k = Registry.CurrentUser.OpenSubKey(LegacyRunKey);
        if (k?.GetValue(TaskName) is not string) return;
        Set(true, exePath);
        AppLog.Info("start with Windows moved from the Run key to a sign-in task");
    }

    public static void Set(bool enabled, string exePath)
    {
        // Older versions used the Run key; remove it so the app is never started twice.
        using (var k = Registry.CurrentUser.OpenSubKey(LegacyRunKey, writable: true))
            k?.DeleteValue(TaskName, throwOnMissingValue: false);

        if (!enabled)
        {
            if (IsEnabled()) SchTasks($"/Delete /TN \"{TaskName}\" /F");
            return;
        }

        var user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts Ubisoft Auto-Login at sign-in (works in Xbox mode too).</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{Escape(user)}</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
                <StartWhenAvailable>true</StartWhenAvailable>
              </Settings>
              <Actions Context="Author"><Exec><Command>{Escape(exePath)}</Command><Arguments>--tray</Arguments></Exec></Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), $"UbisoftAutoLogin-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
            int code = SchTasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F");
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
