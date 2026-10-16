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
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "UbisoftAutoLogin";

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled, string exePath)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) k.SetValue(ValueName, $"\"{exePath}\" --tray");
        else k.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
