namespace UbisoftAutoLogin.Core;

public sealed record WindowInfo(IntPtr Handle, uint Pid, string ProcessName, string? ImagePath, string ClassName,
    string Title, bool Visible, IntPtr Owner, int Left, int Top, int Width, int Height)
{
    public static bool Exists(IntPtr h) => Native.IsWindow(h);

    public static WindowInfo From(IntPtr h)
    {
        uint pid = Native.Pid(h);
        string name;
        try { using var p = System.Diagnostics.Process.GetProcessById((int)pid); name = p.ProcessName; } catch { name = "?"; }
        Native.GetWindowRect(h, out var r);
        return new WindowInfo(h, pid, name, Native.ImagePath(pid), Native.ClassName(h), Native.Title(h),
            Native.IsWindowVisible(h), Native.GetWindow(h, Native.GW_OWNER), r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    /// <summary>Visible top-level windows owned by verified launcher processes.</summary>
    public static List<WindowInfo> LauncherWindows(bool visibleOnly)
    {
        var pids = LauncherVerifier.LauncherProcesses().Select(p => (uint)p.Id).ToHashSet();
        return Native.TopLevelWindows()
            .Where(h => pids.Contains(Native.Pid(h)) && (!visibleOnly || Native.IsWindowVisible(h)))
            .Select(From).ToList();
    }

    public override string ToString() =>
        $"hwnd=0x{Handle.ToInt64():X} pid={Pid} proc={ProcessName} class='{ClassName}' title='{UiaInspector.Sanitize(Title)}' " +
        $"visible={Visible} owner=0x{Owner.ToInt64():X} rect={Left},{Top} {Width}x{Height}";
}
