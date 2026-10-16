namespace UbisoftAutoLogin.Core;

/// <summary>
/// Decides whether a window is the genuine Ubisoft Connect password prompt.
/// All of these must hold: owned by upc.exe; upc.exe lives in the registered install dir, carries a valid
/// Ubisoft Authenticode signature and runs in our session; window class is the verified prompt class;
/// window is visible. The window title is never used.
/// </summary>
public static class PromptDetector
{
    /// <summary>Window class of the "Re-enter your password" prompt, observed on Ubisoft Connect 174.2.</summary>
    public const string PromptWindowClass = "uplay_start";

    public static bool IsVerifiedPrompt(IntPtr hwnd, out ProcessIdentity? identity)
    {
        identity = null;
        if (!Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd)) return false;
        if (!string.Equals(Native.ClassName(hwnd), PromptWindowClass, StringComparison.Ordinal)) return false;
        uint pid = Native.Pid(hwnd);
        if (!LauncherVerifier.IsLauncherPid(pid)) return false;
        identity = LauncherVerifier.Identify(pid);
        return identity.IsTrusted && !identity.UipiBlocked;
    }

    public static List<WindowInfo> FindPrompts() =>
        Native.TopLevelWindows().Where(h => IsVerifiedPrompt(h, out _)).Select(WindowInfo.From).ToList();
}
