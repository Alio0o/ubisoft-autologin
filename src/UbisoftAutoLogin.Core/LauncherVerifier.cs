using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace UbisoftAutoLogin.Core;

public sealed record ProcessIdentity(
    uint Pid,
    string ProcessName,
    string? ImagePath,
    bool PathUnderInstallDir,
    bool SignatureValid,
    string? Signer,
    bool SignerIsUbisoft,
    uint SessionId,
    bool SameSession,
    bool? Elevated,
    bool? SelfElevated)
{
    /// <summary>Every check needed before credentials may be sent to a window of this process.</summary>
    public bool IsTrusted => PathUnderInstallDir && SignatureValid && SignerIsUbisoft && SameSession;

    /// <summary>UIPI blocks a non-elevated client from driving an elevated window.</summary>
    public bool UipiBlocked => Elevated == true && SelfElevated != true;

    public IEnumerable<string> Failures()
    {
        if (ImagePath is null) yield return "image path unreadable";
        if (!PathUnderInstallDir) yield return "not under Ubisoft Connect install dir";
        if (!SignatureValid) yield return "Authenticode signature invalid";
        if (!SignerIsUbisoft) yield return "signer is not Ubisoft";
        if (!SameSession) yield return "different Windows session";
    }
}

/// <summary>
/// Establishes that a process really is the Ubisoft Connect launcher: executable path under the
/// registered install dir, valid Authenticode signature by Ubisoft, same Windows session.
/// Window titles are never used for trust.
/// </summary>
public static class LauncherVerifier
{
    // Verified on a real install (2026-10): upc.exe owns every launcher window; UplayWebCore.exe are CEF helpers.
    public static readonly string[] LauncherExeNames = { "upc.exe" };
    public static readonly string[] HelperExeNames = { "UplayWebCore.exe" };
    private const string SignerOrg = "UBISOFT ENTERTAINMENT";

    private static readonly Dictionary<string, (bool valid, string? signer)> SigCache = new(StringComparer.OrdinalIgnoreCase);

    public static string? InstallDir()
    {
        foreach (var key in new[] { @"SOFTWARE\WOW6432Node\Ubisoft\Launcher", @"SOFTWARE\Ubisoft\Launcher" })
        {
            using var k = Registry.LocalMachine.OpenSubKey(key);
            if (k?.GetValue("InstallDir") is string dir && Directory.Exists(dir))
                return Path.GetFullPath(dir);
        }
        return null;
    }

    public static IEnumerable<Process> LauncherProcesses() =>
        LauncherExeNames.SelectMany(n => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(n)));

    public static ProcessIdentity Identify(uint pid)
    {
        string name;
        try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; } catch { name = "?"; }

        var path = Native.ImagePath(pid);
        var install = InstallDir();
        bool underInstall = path is not null && install is not null
            && LauncherExeNames.Concat(HelperExeNames).Any(n => string.Equals(Path.GetFileName(path), n, StringComparison.OrdinalIgnoreCase))
            && Path.GetFullPath(path).StartsWith(install.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

        bool valid = false; string? signer = null;
        if (path is not null)
        {
            lock (SigCache)
            {
                if (!SigCache.TryGetValue(path, out var cached))
                {
                    bool v = Native.AuthenticodeValid(path);
                    string? s = null;
                    try
                    {
#pragma warning disable SYSLIB0057
                        using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
                        s = cert.Subject;
                    }
                    catch { }
                    cached = (v, s);
                    SigCache[path] = cached;
                }
                (valid, signer) = cached;
            }
        }
        bool signerOk = signer is not null && signer.Contains("O=" + SignerOrg, StringComparison.OrdinalIgnoreCase);

        Native.ProcessIdToSessionId(pid, out var session);
        Native.ProcessIdToSessionId((uint)Environment.ProcessId, out var mySession);

        return new ProcessIdentity(pid, name, path, underInstall, valid, signer, signerOk, session, session == mySession,
            Native.IsElevated(pid), Native.IsElevated((uint)Environment.ProcessId));
    }

    public static bool IsLauncherPid(uint pid)
    {
        var path = Native.ImagePath(pid);
        return path is not null && LauncherExeNames.Any(n => string.Equals(Path.GetFileName(path), n, StringComparison.OrdinalIgnoreCase));
    }
}
