using System.Runtime.InteropServices;

namespace UbisoftAutoLogin.Core;

/// <summary>
/// Types text into one specific window using SendInput (KEYEVENTF_UNICODE).
/// Before every single character it re-checks that the target is still the verified foreground
/// window; if anything else has focus it stops immediately, so keystrokes can never land in
/// another application. No clipboard is used.
/// </summary>
public static class SecureTyper
{
    private const ushort VK_RETURN = 0x0D;

    public static bool EnsureForeground(IntPtr hwnd, bool allowBringToFront)
    {
        if (Native.GetForegroundWindow() == hwnd) return true;
        if (!allowBringToFront) return false;
        Native.SetForegroundWindow(hwnd);
        for (int i = 0; i < 10 && Native.GetForegroundWindow() != hwnd; i++) Thread.Sleep(50);
        return Native.GetForegroundWindow() == hwnd;
    }

    /// <summary>Returns false (and stops typing) as soon as the target is no longer the verified foreground window.</summary>
    public static bool TypeInto(IntPtr hwnd, Func<IntPtr, bool> stillValid, ReadOnlySpan<char> text)
    {
        var inputs = new Native.INPUT[2];
        int size = Marshal.SizeOf<Native.INPUT>();
        try
        {
            foreach (char ch in text)
            {
                if (Native.GetForegroundWindow() != hwnd || !stillValid(hwnd)) return false;
                inputs[0] = Key(0, ch, Native.KEYEVENTF_UNICODE);
                inputs[1] = Key(0, ch, Native.KEYEVENTF_UNICODE | Native.KEYEVENTF_KEYUP);
                if (Native.SendInput(2, inputs, size) != 2) return false;
                Thread.Sleep(8);
            }
            return true;
        }
        finally { Array.Clear(inputs); }
    }

    public static bool PressEnter(IntPtr hwnd, Func<IntPtr, bool> stillValid)
    {
        if (Native.GetForegroundWindow() != hwnd || !stillValid(hwnd)) return false;
        var inputs = new[] { Key(VK_RETURN, '\0', 0), Key(VK_RETURN, '\0', Native.KEYEVENTF_KEYUP) };
        return Native.SendInput(2, inputs, Marshal.SizeOf<Native.INPUT>()) == 2;
    }

    private static Native.INPUT Key(ushort vk, char scan, uint flags) => new()
    {
        type = Native.INPUT_KEYBOARD,
        ki = new Native.KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags }
    };
}
