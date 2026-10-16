using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace UbisoftAutoLogin.Core;

/// <summary>
/// Walks a window's UI Automation tree and produces a sanitized text report.
/// Never reads ValuePattern / TextPattern contents, never reads anything from password fields,
/// and redacts e-mail addresses and long token-like strings from element names.
/// </summary>
public static partial class UiaInspector
{
    public static int MaxDepth { get; set; } = 40;
    public static int MaxNodes { get; set; } = 3000;

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

    public sealed record Node(int Depth, string ControlType, string Name, string AutomationId, string ClassName,
        string FrameworkId, bool IsPassword, bool IsOffscreen, bool IsEnabled, bool HasKeyboardFocus, bool IsKeyboardFocusable,
        string Patterns, string Bounds);

    public static List<Node> Walk(AutomationElement root)
    {
        var nodes = new List<Node>();
        var walker = TreeWalker.RawViewWalker;
        void Visit(AutomationElement el, int depth)
        {
            if (depth > MaxDepth || nodes.Count >= MaxNodes) return;
            nodes.Add(Describe(el, depth));
            AutomationElement? child = null;
            try { child = walker.GetFirstChild(el); } catch { }
            while (child is not null && nodes.Count < MaxNodes)
            {
                Visit(child, depth + 1);
                try { child = walker.GetNextSibling(child); } catch { child = null; }
            }
        }
        Visit(root, 0);
        return nodes;
    }

    public static Node Describe(AutomationElement el, int depth)
    {
        try
        {
            var c = el.Current;
            bool pwd = c.IsPassword;
            // Names of edit fields are normally their label, but a password field's name is never recorded.
            string name = pwd ? "<password-field>" : Sanitize(c.Name);
            var pats = string.Join(",", el.GetSupportedPatterns().Select(p => p.ProgrammaticName.Replace("PatternIdentifiers.Pattern", "")));
            var r = c.BoundingRectangle;
            string bounds = r.IsEmpty ? "-" : $"{(int)r.X},{(int)r.Y} {(int)r.Width}x{(int)r.Height}";
            return new Node(depth, c.ControlType.ProgrammaticName.Replace("ControlType.", ""), name, Sanitize(c.AutomationId, 60),
                c.ClassName, c.FrameworkId, pwd, c.IsOffscreen, c.IsEnabled, c.HasKeyboardFocus, c.IsKeyboardFocusable, pats, bounds);
        }
        catch (Exception ex)
        {
            return new Node(depth, "<error>", ex.GetType().Name, "", "", "", false, false, false, false, false, "", "-");
        }
    }

    public static string Format(IEnumerable<Node> nodes)
    {
        var sb = new StringBuilder();
        foreach (var n in nodes)
        {
            sb.Append(' ', n.Depth * 2)
              .Append('[').Append(n.ControlType).Append(']')
              .Append(" name='").Append(n.Name).Append('\'');
            if (n.AutomationId.Length > 0) sb.Append(" aid='").Append(n.AutomationId).Append('\'');
            if (n.ClassName.Length > 0) sb.Append(" class='").Append(Sanitize(n.ClassName, 60)).Append('\'');
            if (n.FrameworkId.Length > 0) sb.Append(" fw=").Append(n.FrameworkId);
            if (n.IsPassword) sb.Append(" PASSWORD");
            if (n.IsOffscreen) sb.Append(" offscreen");
            if (!n.IsEnabled) sb.Append(" disabled");
            if (n.HasKeyboardFocus) sb.Append(" FOCUSED");
            if (n.Patterns.Length > 0) sb.Append(" pats=").Append(n.Patterns);
            sb.Append(" @").Append(n.Bounds).AppendLine();
        }
        return sb.ToString();
    }
}
