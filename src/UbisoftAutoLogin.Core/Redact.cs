using System.Text.RegularExpressions;

namespace UbisoftAutoLogin.Core;

/// <summary>Redacts e-mail addresses and token-like strings before anything is logged or reported.</summary>
public static partial class Redact
{
    [GeneratedRegex(@"[\w.+\-]+@[\w\-]+(\.[\w\-]+)+")] private static partial Regex EmailRx();
    [GeneratedRegex(@"[A-Za-z0-9+/=_\-]{32,}")] private static partial Regex TokenRx();

    public static string Sanitize(string? s, int max = 80)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = EmailRx().Replace(s, "<email>");
        s = TokenRx().Replace(s, "<long-string>");
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > max ? s[..max] + "…" : s;
    }
}
