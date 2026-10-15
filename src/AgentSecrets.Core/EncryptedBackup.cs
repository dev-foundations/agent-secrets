using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentSecrets;

/// <summary>
/// Portable, password-encrypted backups. Version 1 is: magic (8), salt (16), nonce (12),
/// tag (16), ciphertext. The authenticated header is magic + salt + nonce. The payload is
/// a UTF-8 JSON object mapping aliases to values; neither aliases nor values are public.
/// </summary>
public static class EncryptedBackup
{
    public const int MaxFileBytes = 16 * 1024 * 1024;
    public const int Iterations = 600_000;
    private const int SaltBytes = 16;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int HeaderBytes = 8 + SaltBytes + NonceBytes;
    private const int OverheadBytes = HeaderBytes + TagBytes;
    private static ReadOnlySpan<byte> Magic => "AGSBKP01"u8;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] Encrypt(IReadOnlyDictionary<string, string> secrets, ReadOnlySpan<char> password)
    {
        RequirePassword(password);
        if (secrets.Count == 0)
        {
            throw new BackupException("There are no secrets to export.");
        }

        using var payload = new MemoryStream();
        byte[]? key = null;
        try
        {
            using (var writer = new Utf8JsonWriter(payload))
            {
                writer.WriteStartObject();
                foreach (var (name, value) in secrets)
                {
                    ValidateEntry(name, value);
                    writer.WriteString(name, value);
                    writer.Flush();
                    if (payload.Length > MaxFileBytes - OverheadBytes)
                    {
                        throw new BackupException("The backup exceeds the 16 MiB size limit.");
                    }
                }

                writer.WriteEndObject();
            }

            if (payload.Length > MaxFileBytes - OverheadBytes)
            {
                throw new BackupException("The backup exceeds the 16 MiB size limit.");
            }

            byte[] archive = new byte[OverheadBytes + (int)payload.Length];
            Magic.CopyTo(archive);
            Span<byte> salt = archive.AsSpan(8, SaltBytes);
            Span<byte> nonce = archive.AsSpan(8 + SaltBytes, NonceBytes);
            RandomNumberGenerator.Fill(salt);
            RandomNumberGenerator.Fill(nonce);
            key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
            using var aes = new AesGcm(key, TagBytes);
            aes.Encrypt(nonce, payload.GetBuffer().AsSpan(0, (int)payload.Length),
                archive.AsSpan(OverheadBytes), archive.AsSpan(HeaderBytes, TagBytes), archive.AsSpan(0, HeaderBytes));
            return archive;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload.GetBuffer());
            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }

    public static Dictionary<string, string> Decrypt(ReadOnlySpan<byte> archive, ReadOnlySpan<char> password)
    {
        RequirePassword(password);
        if (archive.Length <= OverheadBytes || archive.Length > MaxFileBytes || !archive[..8].SequenceEqual(Magic))
        {
            throw new BackupException("Invalid or unsupported AgentSecrets backup (expected format version 1, at most 16 MiB).");
        }

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, archive.Slice(8, SaltBytes), Iterations, HashAlgorithmName.SHA256, 32);
        byte[] plaintext = new byte[archive.Length - OverheadBytes];
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            try
            {
                aes.Decrypt(archive.Slice(8 + SaltBytes, NonceBytes), archive[OverheadBytes..],
                    archive.Slice(HeaderBytes, TagBytes), plaintext, archive[..HeaderBytes]);
            }
            catch (CryptographicException)
            {
                throw new BackupException("The password is incorrect or the backup has been damaged or modified. Nothing was imported.");
            }

            using JsonDocument document = JsonDocument.Parse(plaintext);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new BackupException("The backup payload is invalid. Nothing was imported.");
            }

            var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty entry in document.RootElement.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.String)
                {
                    throw new BackupException("The backup contains an invalid secret. Nothing was imported.");
                }

                string value = entry.Value.GetString()!;
                ValidateEntry(entry.Name, value);
                if (!secrets.TryAdd(entry.Name, value))
                {
                    throw new BackupException("The backup contains duplicate aliases. Nothing was imported.");
                }
            }

            if (secrets.Count == 0)
            {
                throw new BackupException("The backup contains no secrets. Nothing was imported.");
            }

            return secrets;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // JsonDocument can defer rejecting incomplete escaped UTF-16 until GetString/Name.
            throw new BackupException("The backup payload is invalid. Nothing was imported.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static void RequirePassword(ReadOnlySpan<char> password)
    {
        if (password.IsEmpty || password.IsWhiteSpace())
        {
            throw new BackupException("The backup password must not be empty or whitespace.");
        }
    }

    private static void ValidateEntry(string name, string value)
    {
        if (!SecretName.IsValid(name) || name != name.ToLowerInvariant() || string.IsNullOrEmpty(value))
        {
            throw new BackupException("The backup contains an invalid alias or empty secret. Nothing was imported.");
        }

        try
        {
            _ = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException)
        {
            // Utf8JsonWriter would replace a broken UTF-16 surrogate and silently change the value.
            throw new BackupException("A secret contains invalid Unicode and cannot be transferred without changing its value.");
        }
    }
}

public sealed class BackupException(string message) : Exception(message);
