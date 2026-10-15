using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace AgentSecrets;

/// <summary>
/// Stores secrets as generic passwords in the user's default (login) keychain: service
/// "AgentSecrets", account = the secret name, label "AgentSecrets/openai" (the name Keychain
/// Access shows). Values are stored as UTF-8. Existence checks and listing read item attributes
/// only, so they never decrypt a value and cannot trigger a Keychain access prompt.
/// </summary>
/// <remarks>
/// The keychain trusts the program that created an item, identified by its code signature.
/// Reading an item from a differently signed build (for example after an upgrade of an ad-hoc
/// signed binary) makes macOS ask the user once per item; the answer is up to them.
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed unsafe partial class MacKeychainStore : ISecretStore
{
    public const string DefaultService = "AgentSecrets";

    private const string SecurityLibrary = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const int errSecSuccess = 0;
    private const int errSecUserCanceled = -128;
    private const int errSecNotAvailable = -25291;
    private const int errSecAuthFailed = -25293;
    private const int errSecNoSuchKeychain = -25294;
    private const int errSecDuplicateItem = -25299;
    private const int errSecItemNotFound = -25300;
    private const int errSecNoDefaultKeychain = -25307;
    private const int errSecInteractionNotAllowed = -25308;

    /// <summary>kSecUnlockStateStatus, from SecKeychainGetStatus.</summary>
    private const uint KeychainUnlocked = 1;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string _service;

    /// <param name="service">Keychain service shared by all items. Tests use their own.</param>
    public MacKeychainStore(string service = DefaultService)
    {
        _service = service;
    }

    public string BackendName => "macOS Keychain";

    public void Set(string name, string value)
    {
        if (value.Length == 0)
        {
            throw new SecretStoreException("The secret value is empty.");
        }

        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(value);
        }
        catch (EncoderFallbackException)
        {
            // Rejected rather than silently altered; the value itself is not echoed.
            throw new SecretStoreException("The secret contains an invalid character (an unpaired UTF-16 surrogate). Nothing was stored.");
        }

        nint data = 0, attributes = 0, query = 0, update = 0;
        try
        {
            fixed (byte* pointer = bytes)
            {
                data = CFDataCreate(0, pointer, bytes.Length);
            }

            attributes = CreateItemQuery(name);
            SetString(attributes, Sec.AttrLabel, $"{_service}/{name}");
            SetString(attributes, Sec.AttrComment, "Managed by AgentSecrets. Injected into child processes by 'agent-secrets run'.");
            CFDictionarySetValue(attributes, Sec.ValueData, data);

            int status = SecItemAdd(attributes, null);
            if (status == errSecDuplicateItem)
            {
                query = CreateItemQuery(name);
                update = CreateDictionary();
                CFDictionarySetValue(update, Sec.ValueData, data);
                status = SecItemUpdate(query, update);
            }

            if (status != errSecSuccess)
            {
                throw Failure("store", name, status);
            }
        }
        finally
        {
            Release(update);
            Release(query);
            Release(attributes);
            Release(data);
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public string? Get(string name)
    {
        nint query = CreateItemQuery(name), data = 0;
        try
        {
            CFDictionarySetValue(query, Sec.ReturnData, Sec.True);
            CFDictionarySetValue(query, Sec.MatchLimit, Sec.MatchLimitOne);
            int status = SecItemCopyMatching(query, out data);
            if (status == errSecItemNotFound)
            {
                return null;
            }

            if (status != errSecSuccess)
            {
                throw Failure("read", name, status);
            }

            int length = data != 0 && CFGetTypeID(data) == CFDataGetTypeID() ? (int)CFDataGetLength(data) : 0;
            if (length == 0)
            {
                return null;
            }

            try
            {
                return StrictUtf8.GetString(CFDataGetBytePtr(data), length);
            }
            catch (DecoderFallbackException)
            {
                throw new SecretStoreException(
                    $"Secret '{name}' was not written by AgentSecrets (unexpected encoding). Re-create it with: agent-secrets set {name}");
            }
        }
        finally
        {
            Release(data);
            Release(query);
        }
    }

    public bool Exists(string name)
    {
        // Attributes only: the value is never decrypted or copied just to check existence.
        nint query = CreateItemQuery(name), result = 0;
        try
        {
            CFDictionarySetValue(query, Sec.ReturnAttributes, Sec.True);
            CFDictionarySetValue(query, Sec.MatchLimit, Sec.MatchLimitOne);
            int status = SecItemCopyMatching(query, out result);
            if (status is errSecSuccess or errSecItemNotFound)
            {
                return status == errSecSuccess;
            }

            throw Failure("look up", name, status);
        }
        finally
        {
            Release(result);
            Release(query);
        }
    }

    public bool Remove(string name)
    {
        nint query = CreateItemQuery(name);
        try
        {
            int status = SecItemDelete(query);
            if (status is errSecSuccess or errSecItemNotFound)
            {
                return status == errSecSuccess;
            }

            throw Failure("remove", name, status);
        }
        finally
        {
            Release(query);
        }
    }

    public IReadOnlyList<string> List()
    {
        var names = new List<string>();
        nint query = CreateItemQuery(null), result = 0;
        try
        {
            CFDictionarySetValue(query, Sec.ReturnAttributes, Sec.True);
            CFDictionarySetValue(query, Sec.MatchLimit, Sec.MatchLimitAll);
            int status = SecItemCopyMatching(query, out result);
            if (status == errSecItemNotFound)
            {
                return names;
            }

            if (status != errSecSuccess)
            {
                throw Failure("enumerate", null, status);
            }

            if (CFGetTypeID(result) != CFArrayGetTypeID())
            {
                throw new SecretStoreException("The macOS Keychain returned an unexpected result while listing secrets.");
            }

            nint count = CFArrayGetCount(result);
            for (nint i = 0; i < count; i++)
            {
                nint item = CFArrayGetValueAtIndex(result, i);
                if (CFGetTypeID(item) != CFDictionaryGetTypeID())
                {
                    continue;
                }

                // Names are stored lower-case. An account in any other form was not created by
                // AgentSecrets and could not be addressed by name, so it is not listed.
                if (ReadString(CFDictionaryGetValue(item, Sec.AttrAccount)) is { } account
                    && SecretName.IsValid(account)
                    && account == account.ToLowerInvariant())
                {
                    names.Add(account);
                }
            }
        }
        finally
        {
            Release(result);
            Release(query);
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>
    /// Whether the default keychain is unlocked, or null when that cannot be determined. A locked
    /// keychain still lists names; reading a value then needs the macOS unlock prompt.
    /// </summary>
    public bool? IsDefaultKeychainUnlocked()
    {
        if (SecKeychainCopyDefault(out nint keychain) != errSecSuccess)
        {
            return null;
        }

        try
        {
            return SecKeychainGetStatus(keychain, out uint status) == errSecSuccess ? (status & KeychainUnlocked) != 0 : null;
        }
        finally
        {
            Release(keychain);
        }
    }

    private nint CreateItemQuery(string? name)
    {
        nint query = CreateDictionary();
        CFDictionarySetValue(query, Sec.Class, Sec.ClassGenericPassword);
        SetString(query, Sec.AttrService, _service);
        if (name is not null)
        {
            SetString(query, Sec.AttrAccount, name);
        }

        return query;
    }

    private static nint CreateDictionary() =>
        CFDictionaryCreateMutable(0, 0, Sec.DictionaryKeyCallBacks, Sec.DictionaryValueCallBacks);

    private static void SetString(nint dictionary, nint key, string value)
    {
        nint text;
        fixed (char* characters = value)
        {
            text = CFStringCreateWithCharacters(0, characters, value.Length);
        }

        CFDictionarySetValue(dictionary, key, text); // The dictionary retains its own reference.
        CFRelease(text);
    }

    private static string? ReadString(nint value)
    {
        if (value == 0 || CFGetTypeID(value) != CFStringGetTypeID())
        {
            return null;
        }

        var characters = new char[(int)CFStringGetLength(value)];
        fixed (char* buffer = characters)
        {
            CFStringGetCharacters(value, new CFRange { Location = 0, Length = characters.Length }, buffer);
        }

        return new string(characters);
    }

    private static void Release(nint value)
    {
        if (value != 0)
        {
            CFRelease(value);
        }
    }

    private static SecretStoreException Failure(string action, string? name, int status)
    {
        string subject = name is null ? "secrets" : $"secret '{name}'";
        string hint = status switch
        {
            errSecInteractionNotAllowed or errSecNotAvailable or errSecNoSuchKeychain or errSecNoDefaultKeychain =>
                " The login keychain is locked or not available in this session (this can happen over SSH or in background jobs)." +
                " Run the command in a terminal of your desktop session, or unlock the keychain first with: security unlock-keychain",
            errSecUserCanceled or errSecAuthFailed =>
                " Access was denied. If macOS asked whether agent-secrets may use the keychain item, run the command again and choose" +
                " 'Always Allow' (needed once per secret after installing or upgrading agent-secrets).",
            _ => "",
        };
        return new SecretStoreException($"The macOS Keychain could not {action} {subject} (OSStatus {status}{Describe(status)}).{hint}");
    }

    private static string Describe(int status)
    {
        nint message = SecCopyErrorMessageString(status, 0);
        try
        {
            return ReadString(message) is { Length: > 0 } text ? $": {text.TrimEnd('.')}" : "";
        }
        finally
        {
            Release(message);
        }
    }

    /// <summary>Framework constants (CFStringRef and friends), resolved once.</summary>
    private static class Sec
    {
        private static readonly nint Security = NativeLibrary.Load(SecurityLibrary);
        private static readonly nint CoreFoundation = NativeLibrary.Load(CoreFoundationLibrary);

        public static readonly nint Class = Constant(Security, "kSecClass");
        public static readonly nint ClassGenericPassword = Constant(Security, "kSecClassGenericPassword");
        public static readonly nint AttrService = Constant(Security, "kSecAttrService");
        public static readonly nint AttrAccount = Constant(Security, "kSecAttrAccount");
        public static readonly nint AttrLabel = Constant(Security, "kSecAttrLabel");
        public static readonly nint AttrComment = Constant(Security, "kSecAttrComment");
        public static readonly nint ValueData = Constant(Security, "kSecValueData");
        public static readonly nint ReturnData = Constant(Security, "kSecReturnData");
        public static readonly nint ReturnAttributes = Constant(Security, "kSecReturnAttributes");
        public static readonly nint MatchLimit = Constant(Security, "kSecMatchLimit");
        public static readonly nint MatchLimitOne = Constant(Security, "kSecMatchLimitOne");
        public static readonly nint MatchLimitAll = Constant(Security, "kSecMatchLimitAll");
        public static readonly nint True = Constant(CoreFoundation, "kCFBooleanTrue");

        // These two are structs: what the dictionary functions take is their address.
        public static readonly nint DictionaryKeyCallBacks = NativeLibrary.GetExport(CoreFoundation, "kCFTypeDictionaryKeyCallBacks");
        public static readonly nint DictionaryValueCallBacks = NativeLibrary.GetExport(CoreFoundation, "kCFTypeDictionaryValueCallBacks");

        private static nint Constant(nint library, string symbol) => *(nint*)NativeLibrary.GetExport(library, symbol);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CFRange
    {
        public nint Location;
        public nint Length;
    }

    [LibraryImport(SecurityLibrary)]
    private static partial int SecItemAdd(nint attributes, nint* result);

    [LibraryImport(SecurityLibrary)]
    private static partial int SecItemCopyMatching(nint query, out nint result);

    [LibraryImport(SecurityLibrary)]
    private static partial int SecItemUpdate(nint query, nint attributesToUpdate);

    [LibraryImport(SecurityLibrary)]
    private static partial int SecItemDelete(nint query);

    [LibraryImport(SecurityLibrary)]
    private static partial nint SecCopyErrorMessageString(int status, nint reserved);

    [LibraryImport(SecurityLibrary)]
    private static partial int SecKeychainCopyDefault(out nint keychain);

    [LibraryImport(SecurityLibrary)]
    private static partial int SecKeychainGetStatus(nint keychain, out uint status);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nint CFDictionaryCreateMutable(nint allocator, nint capacity, nint keyCallBacks, nint valueCallBacks);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial void CFDictionarySetValue(nint dictionary, nint key, nint value);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nint CFDictionaryGetValue(nint dictionary, nint key);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nint CFStringCreateWithCharacters(nint allocator, char* characters, nint length);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nint CFStringGetLength(nint text);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial void CFStringGetCharacters(nint text, CFRange range, char* buffer);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nint CFDataCreate(nint allocator, byte* bytes, nint length);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nint CFDataGetLength(nint data);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial byte* CFDataGetBytePtr(nint data);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nint CFArrayGetCount(nint array);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nint CFArrayGetValueAtIndex(nint array, nint index);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nuint CFGetTypeID(nint value);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nuint CFArrayGetTypeID();

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nuint CFDictionaryGetTypeID();

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nuint CFStringGetTypeID();

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nuint CFDataGetTypeID();

    [LibraryImport(CoreFoundationLibrary)]
    private static partial void CFRelease(nint value);
}
