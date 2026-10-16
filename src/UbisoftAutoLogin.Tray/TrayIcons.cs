using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using UbisoftAutoLogin.Core;

namespace UbisoftAutoLogin.Tray;

/// <summary>
/// Tray icon = the installed Ubisoft Connect icon (read at runtime from upc.exe on this PC, never shipped
/// with this project) plus a small status dot. Falls back to a plain drawn icon if Ubisoft Connect is missing.
/// </summary>
internal static class TrayIcons
{
    private static readonly Dictionary<EngineStatus, Icon> Cache = new();
    private static Bitmap? _base;

    public static Icon For(EngineStatus status)
    {
        if (Cache.TryGetValue(status, out var icon)) return icon;
        int size = SystemInformation.SmallIconSize.Width * 2; // draw at 2x for HiDPI trays
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var baseImg = BaseImage();
            if (status == EngineStatus.Disabled)
                ControlPaint.DrawImageDisabled(g, new Bitmap(baseImg, size, size), 0, 0, Color.Transparent);
            else
                g.DrawImage(baseImg, 0, 0, size, size);

            var dot = status switch
            {
                EngineStatus.ManualActionRequired or EngineStatus.Paused => Color.FromArgb(255, 176, 32),
                EngineStatus.LoginInProgress => Color.FromArgb(64, 156, 255),
                EngineStatus.Disabled => Color.Empty,
                _ => Color.FromArgb(46, 204, 113)
            };
            if (!dot.IsEmpty)
            {
                int d = size * 7 / 16;
                var r = new Rectangle(size - d - 1, size - d - 1, d, d);
                using var ring = new SolidBrush(Color.FromArgb(20, 20, 20));
                g.FillEllipse(ring, Rectangle.Inflate(r, size / 16, size / 16));
                using var fill = new SolidBrush(dot);
                g.FillEllipse(fill, r);
            }
        }
        var h = bmp.GetHicon();
        icon = (Icon)Icon.FromHandle(h).Clone();
        DestroyIcon(h);
        Cache[status] = icon;
        return icon;
    }

    private static Bitmap BaseImage()
    {
        if (_base is not null) return _base;
        try
        {
            var dir = LauncherVerifier.InstallDir();
            var exe = dir is null ? null : Path.Combine(dir, "upc.exe");
            if (exe is not null && File.Exists(exe))
            {
                using var ico = Icon.ExtractIcon(exe, 0, 64) ?? Icon.ExtractAssociatedIcon(exe);
                if (ico is not null) return _base = ico.ToBitmap();
            }
        }
        catch (Exception ex) { AppLog.Warn("could not read the Ubisoft Connect icon: " + ex.GetType().Name); }
        return _base = Fallback();
    }

    /// <summary>Simple original icon: dark circle with a white controller-style "play" mark.</summary>
    private static Bitmap Fallback()
    {
        var b = new Bitmap(64, 64);
        using var g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var bg = new SolidBrush(Color.FromArgb(0, 112, 255));
        g.FillEllipse(bg, 2, 2, 60, 60);
        using var fg = new SolidBrush(Color.White);
        g.FillPolygon(fg, new[] { new Point(25, 18), new Point(25, 46), new Point(47, 32) });
        return b;
    }

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);
}
