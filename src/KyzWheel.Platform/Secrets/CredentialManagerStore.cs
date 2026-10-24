using System.Runtime.InteropServices;
using System.Text;
using KyzWheel.Mail;

namespace KyzWheel.Platform.Secrets;

public sealed unsafe partial class CredentialManagerStore : ISecretStore
{
    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public char* TargetName;
        public char* Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public byte* CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public char* TargetAlias;
        public char* UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(CREDENTIAL* cred, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, uint type, uint flags, out CREDENTIAL* cred);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target, uint type, uint flags);

    [LibraryImport("advapi32.dll")]
    private static partial void CredFree(void* buffer);

    public void Set(string key, string secret)
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        fixed (char* target = key)
        fixed (char* user = "kyzWheel")
        fixed (byte* data = blob)
        {
            var c = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                UserName = user,
                CredentialBlob = data,
                CredentialBlobSize = (uint)blob.Length,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
            };
            if (!CredWrite(&c, 0)) throw new InvalidOperationException($"No se pudo guardar la contraseña ({Marshal.GetLastPInvokeError()})");
        }
        Array.Clear(blob);
    }

    public string? Get(string key)
    {
        if (!CredRead(key, CRED_TYPE_GENERIC, 0, out var c)) return null;
        try
        {
            return c->CredentialBlobSize == 0 ? "" : new string((char*)c->CredentialBlob, 0, (int)c->CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(c);
        }
    }

    public void Remove(string key) => CredDelete(key, CRED_TYPE_GENERIC, 0);
}
