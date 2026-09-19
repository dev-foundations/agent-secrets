using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace AgentSecrets;

/// <summary>
/// Stores secrets as Generic Credentials in Windows Credential Manager under
/// target names like "AgentSecrets/openai". Values are stored as UTF-16, the same
/// convention the Windows Credential Manager UI uses.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class WindowsCredentialStore : ISecretStore
{
    public const string DefaultTargetPrefix = "AgentSecrets/";

    /// <summary>CRED_MAX_CREDENTIAL_BLOB_SIZE is 2560 bytes; values are UTF-16.</summary>
    public const int MaxSecretChars = 2560 / sizeof(char);

    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;
    private const int ERROR_NOT_FOUND = 1168;
    private const int ERROR_NO_SUCH_LOGON_SESSION = 1312;

    private readonly string _prefix;

    /// <param name="targetPrefix">Namespace for target names. Tests use their own prefix.</param>
    public WindowsCredentialStore(string targetPrefix = DefaultTargetPrefix)
    {
        _prefix = targetPrefix;
    }

    public string BackendName => "Windows Credential Manager";

    public void Set(string name, string value)
    {
        if (value.Length == 0)
        {
            throw new SecretStoreException("The secret value is empty.");
        }

        if (value.Length > MaxSecretChars)
        {
            throw new SecretStoreException(
                $"The secret is too long for Windows Credential Manager (limit: {MaxSecretChars} characters).");
        }

        byte[] blob = Encoding.Unicode.GetBytes(value);
        try
        {
            fixed (byte* blobPtr = blob)
            fixed (char* target = _prefix + name)
            fixed (char* userName = "agent-secrets")
            fixed (char* comment = "Managed by AgentSecrets. Injected into child processes by 'agent-secrets run'.")
            {
                var credential = new CREDENTIAL
                {
                    Type = CRED_TYPE_GENERIC,
                    TargetName = target,
                    Comment = comment,
                    CredentialBlobSize = (uint)blob.Length,
                    CredentialBlob = blobPtr,
                    Persist = CRED_PERSIST_LOCAL_MACHINE,
                    UserName = userName,
                };

                if (!CredWriteW(&credential, 0))
                {
                    throw Failure("store", name, Marshal.GetLastPInvokeError());
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
        }
    }

    public string? Get(string name)
    {
        if (!CredReadW(_prefix + name, CRED_TYPE_GENERIC, 0, out CREDENTIAL* credential))
        {
            int error = Marshal.GetLastPInvokeError();
            return error == ERROR_NOT_FOUND ? null : throw Failure("read", name, error);
        }

        try
        {
            if (credential->CredentialBlob is null || credential->CredentialBlobSize == 0)
            {
                return null;
            }

            if (credential->CredentialBlobSize % sizeof(char) != 0)
            {
                throw new SecretStoreException(
                    $"Secret '{name}' was not written by AgentSecrets (unexpected encoding). Re-create it with: agent-secrets set {name}");
            }

            return new string((char*)credential->CredentialBlob, 0, (int)credential->CredentialBlobSize / sizeof(char));
        }
        finally
        {
            CredFree(credential);
        }
    }

    public bool Exists(string name)
    {
        // Enumerate with an exact filter and look only at target names, so the
        // secret value is never copied into managed memory just to check existence.
        return EnumerateTargetNames(_prefix + name).Count > 0;
    }

    public bool Remove(string name)
    {
        if (CredDeleteW(_prefix + name, CRED_TYPE_GENERIC, 0))
        {
            return true;
        }

        int error = Marshal.GetLastPInvokeError();
        return error == ERROR_NOT_FOUND ? false : throw Failure("remove", name, error);
    }

    public IReadOnlyList<string> List()
    {
        var names = new List<string>();
        foreach (string target in EnumerateTargetNames(_prefix + "*"))
        {
            string candidate = target[_prefix.Length..];
            if (SecretName.IsValid(candidate))
            {
                names.Add(candidate.ToLowerInvariant());
            }
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private List<string> EnumerateTargetNames(string filter)
    {
        var targets = new List<string>();
        if (!CredEnumerateW(filter, 0, out uint count, out CREDENTIAL** credentials))
        {
            int error = Marshal.GetLastPInvokeError();
            return error == ERROR_NOT_FOUND ? targets : throw Failure("enumerate", null, error);
        }

        try
        {
            for (uint i = 0; i < count; i++)
            {
                CREDENTIAL* credential = credentials[i];
                if (credential->Type != CRED_TYPE_GENERIC || credential->TargetName is null)
                {
                    continue;
                }

                string target = new(credential->TargetName);
                if (target.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase))
                {
                    targets.Add(target);
                }
            }
        }
        finally
        {
            CredFree(credentials);
        }

        return targets;
    }

    private static SecretStoreException Failure(string action, string? name, int error)
    {
        string subject = name is null ? "secrets" : $"secret '{name}'";
        string hint = error == ERROR_NO_SUCH_LOGON_SESSION
            ? " Credential Manager is not available in this logon session (this can happen over SSH, in services or scheduled tasks)."
            : "";
        return new SecretStoreException($"Windows Credential Manager could not {action} {subject} (Win32 error {error}).{hint}");
    }

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
        public void* Attributes;
        public char* TargetAlias;
        public char* UserName;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWriteW(CREDENTIAL* credential, uint flags);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredReadW(string targetName, uint type, uint flags, out CREDENTIAL* credential);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredEnumerateW(string? filter, uint flags, out uint count, out CREDENTIAL** credentials);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDeleteW(string targetName, uint type, uint flags);

    [LibraryImport("advapi32.dll")]
    private static partial void CredFree(void* buffer);
}
