using System.Runtime.InteropServices;

namespace UbisoftAutoLogin.Core;

/// <summary>
/// Stores the Ubisoft e-mail and password as a Generic credential in Windows Credential Manager
/// (CredWrite/CredRead/CredDelete). Windows encrypts it with the current user's DPAPI key, so other
/// Windows users cannot read it. Any program running as the same Windows user can, which is the
/// inherent limit of local credential storage.
/// </summary>
public sealed class CredentialStore
{
    public const string DefaultTarget = "UbisoftAutoLogin:UbisoftConnect";
    private readonly string _target;

    public CredentialStore(string target = DefaultTarget) => _target = target;

    public bool Exists()
    {
        if (!CredRead(_target, CRED_TYPE_GENERIC, 0, out var p)) return false;
        CredFree(p);
        return true;
    }

    public string? ReadUserName()
    {
        if (!CredRead(_target, CRED_TYPE_GENERIC, 0, out var p)) return null;
        try { return Marshal.PtrToStructure<CREDENTIAL>(p).UserName; }
        finally { CredFree(p); }
    }

    /// <summary>Saves the credential. The caller should clear <paramref name="password"/> afterwards.</summary>
    public void Save(string userName, ReadOnlySpan<char> password)
    {
        if (password.IsEmpty) throw new ArgumentException("Password is empty.");
        int bytes = password.Length * 2;
        IntPtr blob = Marshal.AllocHGlobal(bytes);
        try
        {
            unsafe { fixed (char* src = password) Buffer.MemoryCopy(src, (void*)blob, bytes, bytes); }
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = _target,
                UserName = userName,
                CredentialBlobSize = (uint)bytes,
                CredentialBlob = blob,
                Persist = CRED_PERSIST_LOCAL_MACHINE, // this user, this PC only (no roaming)
                Comment = "Ubisoft Auto-Login"
            };
            if (!CredWrite(ref cred, 0))
                throw new InvalidOperationException("CredWrite failed (Win32 error " + Marshal.GetLastWin32Error() + ").");
        }
        finally
        {
            unsafe { new Span<byte>((void*)blob, bytes).Clear(); }
            Marshal.FreeHGlobal(blob);
        }
    }

    /// <summary>
    /// Reads the password into a char array that the caller MUST clear (Array.Clear) after use.
    /// Returns null when no credential is stored.
    /// </summary>
    public char[]? ReadPassword()
    {
        if (!CredRead(_target, CRED_TYPE_GENERIC, 0, out var p)) return null;
        try
        {
            var c = Marshal.PtrToStructure<CREDENTIAL>(p);
            if (c.CredentialBlob == IntPtr.Zero || c.CredentialBlobSize == 0) return null;
            var chars = new char[c.CredentialBlobSize / 2];
            Marshal.Copy(c.CredentialBlob, chars, 0, chars.Length);
            unsafe { new Span<byte>((void*)c.CredentialBlob, (int)c.CredentialBlobSize).Clear(); }
            return chars;
        }
        finally { CredFree(p); }
    }

    public bool Delete() => CredDelete(_target, CRED_TYPE_GENERIC, 0);

    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags; public uint Type; public string TargetName; public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist;
        public uint AttributeCount; public IntPtr Attributes; public string? TargetAlias; public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
    private static extern bool CredWrite(ref CREDENTIAL cred, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr cred);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredDeleteW")]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr cred);
}
