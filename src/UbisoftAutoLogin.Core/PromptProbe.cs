using System.Windows.Automation;

namespace UbisoftAutoLogin.Core;

public enum PromptKind { Unknown, NoPrompt, PasswordLogin, TwoFactor, Captcha, AccountRecovery, NetworkError }

public sealed record ProbeResult(WindowInfo Window, ProcessIdentity Identity, PromptKind Kind, int PasswordFields,
    int EditFields, IReadOnlyList<string> ButtonNames, string Note);

/// <summary>
/// Inspects one window and locates (but never fills) login controls.
/// The classification heuristics are provisional until a real prompt has been captured.
/// </summary>
public static class PromptProbe
{
    public static ProbeResult Probe(IntPtr hwnd, bool requireLauncher = true)
    {
        var win = WindowInfo.From(hwnd);
        var id = LauncherVerifier.Identify(win.Pid);
        bool ok = requireLauncher ? id.IsTrusted : id.IsTrusted || (id.SignatureValid && id.SignerIsUbisoft);
        if (!ok)
            return new ProbeResult(win, id, PromptKind.Unknown, 0, 0, Array.Empty<string>(), "untrusted process: " + string.Join("; ", id.Failures()));

        var root = AutomationElement.FromHandle(hwnd);
        var nodes = UiaInspector.Walk(root);
        int pwd = nodes.Count(n => n.ControlType == "Edit" && n.IsPassword && !n.IsOffscreen);
        int edits = nodes.Count(n => n.ControlType == "Edit" && !n.IsOffscreen);
        var buttons = nodes.Where(n => n.ControlType == "Button" && !n.IsOffscreen && n.Name.Length > 0).Select(n => n.Name).Distinct().ToList();
        var text = string.Join(" ", nodes.Select(n => n.Name)).ToLowerInvariant();

        // Provisional: order matters, the safest (non-automatable) states win.
        PromptKind kind =
            text.Contains("captcha") || text.Contains("i'm not a robot") ? PromptKind.Captcha :
            text.Contains("verification code") || text.Contains("two-step") || text.Contains("2-step") || text.Contains("authenticator") ? PromptKind.TwoFactor :
            text.Contains("reset your password") || text.Contains("recover") ? PromptKind.AccountRecovery :
            text.Contains("connection") && (text.Contains("lost") || text.Contains("offline") || text.Contains("unable")) ? PromptKind.NetworkError :
            pwd == 1 ? PromptKind.PasswordLogin :
            pwd == 0 ? PromptKind.NoPrompt : PromptKind.Unknown;

        string note = id.UipiBlocked ? "target is elevated; run this tool elevated or UI Automation input will be blocked" : "";
        return new ProbeResult(win, id, kind, pwd, edits, buttons, note);
    }

    /// <summary>
    /// Phase-1 prototype: finds the single visible password field in a verified launcher window and gives it keyboard focus.
    /// Enters nothing. Returns a human-readable result.
    /// </summary>
    public static string FocusPasswordField(IntPtr hwnd)
    {
        var win = WindowInfo.From(hwnd);
        var id = LauncherVerifier.Identify(win.Pid);
        if (!id.IsTrusted) return "Refused: window is not a verified Ubisoft Connect window (" + string.Join("; ", id.Failures()) + ")";
        if (!LauncherVerifier.IsLauncherPid(win.Pid)) return "Refused: window is not owned by upc.exe";

        var root = AutomationElement.FromHandle(hwnd);
        var cond = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new PropertyCondition(AutomationElement.IsPasswordProperty, true));
        var fields = root.FindAll(TreeScope.Descendants, cond).Cast<AutomationElement>().Where(e => !e.Current.IsOffscreen).ToList();
        if (fields.Count == 0) return "No visible password field found (UI Automation exposes none in this window).";
        if (fields.Count > 1) return $"Refused: {fields.Count} password fields visible; ambiguous.";

        var f = fields[0];
        try
        {
            f.SetFocus();
            bool focused = f.Current.HasKeyboardFocus;
            bool hasValuePattern = f.TryGetCurrentPattern(ValuePattern.Pattern, out _);
            return $"Password field found and focused={focused}. ValuePattern={(hasValuePattern ? "yes" : "no")}, aid='{f.Current.AutomationId}'. Nothing was typed.";
        }
        catch (Exception ex) { return "Found password field but SetFocus failed: " + ex.GetType().Name; }
    }
}
