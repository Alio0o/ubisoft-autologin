namespace UbisoftAutoLogin.Core;

/// <summary>
/// Event-driven detection of top-level windows being shown or brought to the foreground.
/// Uses an out-of-context WinEvent hook (no code injection). Must be created on a thread
/// that pumps messages (e.g. the WinForms UI thread).
/// </summary>
public sealed class WindowWatcher : IDisposable
{
    private readonly Native.WinEventProc _proc; // keep delegate alive
    private IntPtr _showHook, _fgHook;

    /// <summary>Raised on the hooking thread with a top-level window handle.</summary>
    public event Action<IntPtr, string>? WindowEvent;

    public WindowWatcher()
    {
        _proc = OnEvent;
    }

    public void Start()
    {
        if (_showHook != IntPtr.Zero) return;
        uint flags = Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS;
        _showHook = Native.SetWinEventHook(Native.EVENT_OBJECT_SHOW, Native.EVENT_OBJECT_SHOW, IntPtr.Zero, _proc, 0, 0, flags);
        _fgHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, flags);
    }

    public void Stop()
    {
        if (_showHook != IntPtr.Zero) { Native.UnhookWinEvent(_showHook); _showHook = IntPtr.Zero; }
        if (_fgHook != IntPtr.Zero) { Native.UnhookWinEvent(_fgHook); _fgHook = IntPtr.Zero; }
    }

    private void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || idObject != Native.OBJID_WINDOW || idChild != 0) return;
        if (Native.GetAncestor(hwnd, 2 /*GA_ROOT*/) != hwnd) return; // top-level only
        WindowEvent?.Invoke(hwnd, evt == Native.EVENT_SYSTEM_FOREGROUND ? "foreground" : "show");
    }

    public void Dispose() => Stop();
}
