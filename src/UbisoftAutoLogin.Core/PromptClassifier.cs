using System.Drawing;
using System.Drawing.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace UbisoftAutoLogin.Core;

public enum PromptKind { Unknown, NoPrompt, PasswordLogin, TwoFactor, Captcha, AccountRecovery, NetworkError }

public sealed record Classification(PromptKind Kind, bool EmailPrefilled, bool PasswordFieldEmpty, string Reason)
{
    public bool SafeToAutoFill => Kind == PromptKind.PasswordLogin && EmailPrefilled && PasswordFieldEmpty;
}

/// <summary>
/// Ubisoft Connect's re-login prompt (window class "uplay_start") is custom-drawn and exposes no
/// UI Automation children, so its content is read with the built-in Windows OCR engine instead.
/// The window image and recognised text stay in memory only: nothing is saved or logged except
/// the resulting classification.
/// </summary>
public static class PromptClassifier
{
    // Verified on Ubisoft Connect 174.2 (English UI): heading "Re-enter your password",
    // labels "E-mail:" / "Password:", placeholder "Password", links "Recover password" / "Log in".
    private static readonly string[] CaptchaWords = { "captcha", "robot", "puzzle", "verify you are human" };
    private static readonly string[] TwoFactorWords = { "verification code", "2-step", "two-step", "authenticator", "security code", "enter the code" };
    private static readonly string[] RecoveryWords = { "reset your password", "forgot your password", "account recovery" };
    private static readonly string[] NetworkWords = { "no internet", "offline mode", "unable to connect", "connection lost", "connection error" };

    public static async Task<Classification> ClassifyAsync(IntPtr hwnd)
    {
        var lines = await OcrWindowAsync(hwnd);
        if (lines is null) return new(PromptKind.Unknown, false, false, "OCR unavailable or capture failed");
        return Classify(lines);
    }

    /// <summary>Pure text classification; kept separate so it can be unit tested.</summary>
    public static Classification Classify(IReadOnlyList<string> lines)
    {
        var all = string.Join("\n", lines).ToLowerInvariant();
        bool Has(IEnumerable<string> words) => words.Any(all.Contains);

        if (Has(CaptchaWords)) return new(PromptKind.Captcha, false, false, "captcha wording found");
        if (Has(TwoFactorWords)) return new(PromptKind.TwoFactor, false, false, "two-factor wording found");
        if (Has(RecoveryWords)) return new(PromptKind.AccountRecovery, false, false, "recovery wording found");
        if (Has(NetworkWords)) return new(PromptKind.NetworkError, false, false, "network error wording found");

        bool heading = all.Contains("re-enter your password") || all.Contains("enter your password");
        bool passwordLabel = lines.Any(l => l.Trim().TrimEnd(':').Equals("password", StringComparison.OrdinalIgnoreCase));
        // OCR often splits the white-on-blue "Log in" button text, so the "Recover password" link counts as the
        // second marker of this screen.
        bool loginButton = all.Contains("log in") || all.Contains("login") || all.Contains("recover password");
        if (!(heading && passwordLabel && loginButton))
            return new(PromptKind.Unknown, false, false, $"not the known password prompt (heading={heading} label={passwordLabel} button={loginButton})");

        bool email = lines.Any(l => l.Contains('@'));
        // "Password" appears once as the label ("Password:") and once as the placeholder of an empty field.
        int passwordWords = lines.Count(l => l.Trim().TrimEnd(':').Equals("password", StringComparison.OrdinalIgnoreCase));
        bool empty = passwordWords >= 2;
        return new(PromptKind.PasswordLogin, email, empty,
            $"password prompt (emailPrefilled={email}, passwordFieldEmpty={empty})");
    }

    /// <summary>OCR of a window. Callers must not persist or log the raw text.</summary>
    public static async Task<List<string>?> OcrWindowAsync(IntPtr hwnd)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null) { AppLog.Warn("Windows OCR has no language available"); return null; }

        // Capture in physical pixels regardless of how the host process is DPI-configured.
        var oldCtx = Native.SetThreadDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        Bitmap bmp;
        int w, h;
        try
        {
            if (!Native.GetWindowRect(hwnd, out var r)) return null;
            w = r.Right - r.Left; h = r.Bottom - r.Top;
            if (w < 50 || h < 50) return null;
            bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            var hdc = g.GetHdc();
            bool ok = Native.PrintWindow(hwnd, hdc, Native.PW_RENDERFULLCONTENT);
            g.ReleaseHdc(hdc);
            if (!ok) { bmp.Dispose(); return null; }
        }
        finally { Native.SetThreadDpiAwarenessContext(oldCtx); }
        using var _ = bmp;

        // Scale down very large (HiDPI) captures to stay within the OCR engine's limit.
        double scale = Math.Min(1.0, OcrEngine.MaxImageDimension / (double)Math.Max(w, h));
        using var input = scale < 1.0 ? new Bitmap(bmp, (int)(w * scale), (int)(h * scale)) : (Bitmap)bmp.Clone();

        using var ms = new MemoryStream();
        input.Save(ms, ImageFormat.Bmp);
        ms.Position = 0;
        var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
        using var soft = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(soft);
        return result.Lines.Select(l => l.Text).ToList();
    }
}
