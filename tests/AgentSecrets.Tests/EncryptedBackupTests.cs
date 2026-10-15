using System.Text;
using System.Security.Cryptography;

namespace AgentSecrets.Tests;

public class EncryptedBackupTests
{
    private const string Password = "  FAKE backup passphrase ñ 日本 🔐  ";

    [Fact]
    public void Reads_a_portable_v1_vector_from_an_independent_implementation()
    {
        // Node.js/OpenSSL: pbkdf2Sync(password, salt, 600000, 32, 'sha256'),
        // createCipheriv('aes-256-gcm', key, nonce), setAAD(header), tag before ciphertext.
        // Salt = bytes 0..15; nonce = bytes 32..43. This fixture runs unchanged on both OSes.
        const string vector = "QUdTQktQMDEAAQIDBAUGBwgJCgsMDQ4PICEiIyQlJicoKSorCMQQnNnlJjfZ3clYu/eddIMxv2npxt5UrpRWUzrfcal9fQi0/eBFe7gvjXRyjQYL+uD+WkeJd/AocUMXOgDi9djshfk/LcfTPX9wVKtr4VXErTZgz5bN";
        var restored = EncryptedBackup.Decrypt(Convert.FromBase64String(vector), "  FAKE portable passphrase ñ 日本 🔐  ");

        Assert.Equal("FAKE-PORTABLE-VALUE-ñ-日本", restored["openai"]);
        Assert.Equal("FAKE-SECOND-VALUE", restored["github"]);
    }

    [Fact]
    public void Round_trip_preserves_aliases_values_and_password_exactly()
    {
        var secrets = new Dictionary<string, string> { ["openai"] = Fake.Secret() + " ñ✓ 日本\nwith spaces ", ["github-work"] = Fake.Secret() };
        byte[] archive = EncryptedBackup.Encrypt(secrets, Password);
        Dictionary<string, string> restored = EncryptedBackup.Decrypt(archive, Password);

        Assert.Equal(secrets.OrderBy(p => p.Key), restored.OrderBy(p => p.Key));
        string fileText = Encoding.UTF8.GetString(archive);
        Assert.DoesNotContain("openai", fileText);
        Assert.DoesNotContain(secrets["openai"], fileText);
        Assert.DoesNotContain(Password, fileText);
        Assert.Throws<BackupException>(() => EncryptedBackup.Decrypt(archive, Password.Trim()));
    }

    [Fact]
    public void Each_export_uses_a_fresh_salt_and_nonce()
    {
        var secrets = new Dictionary<string, string> { ["openai"] = Fake.Secret() };
        byte[] first = EncryptedBackup.Encrypt(secrets, Password);
        byte[] second = EncryptedBackup.Encrypt(secrets, Password);

        Assert.False(first.AsSpan(8, 16).SequenceEqual(second.AsSpan(8, 16)));
        Assert.False(first.AsSpan(24, 12).SequenceEqual(second.AsSpan(24, 12)));
        Assert.Equal(secrets["openai"], EncryptedBackup.Decrypt(second, Password)["openai"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(36)]
    [InlineData(52)]
    public void Rejects_modification_of_version_salt_nonce_tag_and_ciphertext(int offset)
    {
        byte[] archive = EncryptedBackup.Encrypt(new Dictionary<string, string> { ["openai"] = Fake.Secret() }, Password);
        archive[offset] ^= 1;

        Assert.Throws<BackupException>(() => EncryptedBackup.Decrypt(archive, Password));
    }

    [Fact]
    public void Rejects_wrong_password_truncation_and_appended_data()
    {
        byte[] archive = EncryptedBackup.Encrypt(new Dictionary<string, string> { ["openai"] = Fake.Secret() }, Password);

        Assert.Throws<BackupException>(() => EncryptedBackup.Decrypt(archive, "wrong password"));
        Assert.Throws<BackupException>(() => EncryptedBackup.Decrypt(archive[..^1], Password));
        Assert.Throws<BackupException>(() => EncryptedBackup.Decrypt([.. archive, 0], Password));
        Assert.Throws<BackupException>(() => EncryptedBackup.Decrypt([], Password));
        Assert.Throws<BackupException>(() => EncryptedBackup.Decrypt(new byte[EncryptedBackup.MaxFileBytes + 1], Password));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    public void Rejects_empty_passwords(string password)
    {
        Assert.Throws<BackupException>(() => EncryptedBackup.Encrypt(new Dictionary<string, string> { ["openai"] = Fake.Secret() }, password));
    }

    [Fact]
    public void Rejects_empty_backups_and_invalid_aliases_without_echoing_values()
    {
        Assert.Throws<BackupException>(() => EncryptedBackup.Encrypt(new Dictionary<string, string>(), Password));
        string secret = Fake.Secret();
        var error = Assert.Throws<BackupException>(() => EncryptedBackup.Encrypt(new Dictionary<string, string> { ["invalid/alias"] = secret }, Password));
        Assert.DoesNotContain(secret, error.Message);
        Assert.DoesNotContain(Password, error.Message);
    }

    [Fact]
    public void Refuses_invalid_unicode_instead_of_silently_changing_a_secret()
    {
        Assert.Throws<BackupException>(() => EncryptedBackup.Encrypt(new Dictionary<string, string> { ["openai"] = "FAKE-\uD800" }, Password));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not-json")]
    [InlineData("{\"openai\":\"a\",\"openai\":\"b\"}")]
    [InlineData("{\"openai\":\"a\",\"OPENAI\":\"b\"}")]
    [InlineData("{\"invalid/alias\":\"FAKE\"}")]
    [InlineData("{\"openai\":42}")]
    [InlineData("{\"openai\":\"\"}")]
    [InlineData("{\"openai\":\"\\uD800\"}")]
    public void Rejects_invalid_payloads_even_with_valid_authentication(string json)
    {
        // Construct an authenticated external file directly, bypassing the exporter's validation.
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] archive = new byte[52 + payload.Length];
        "AGSBKP01"u8.CopyTo(archive);
        RandomNumberGenerator.Fill(archive.AsSpan(8, 28));
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(Password, archive.AsSpan(8, 16), 600_000, HashAlgorithmName.SHA256, 32);
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(archive.AsSpan(24, 12), payload, archive.AsSpan(52), archive.AsSpan(36, 16), archive.AsSpan(0, 36));

        Assert.Throws<BackupException>(() => EncryptedBackup.Decrypt(archive, Password));
    }
}
