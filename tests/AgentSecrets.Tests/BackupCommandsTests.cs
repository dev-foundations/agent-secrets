using System.Text;
using AgentSecrets.Cli;

namespace AgentSecrets.Tests;

public class BackupCommandsTests
{
    private const string Password = "FAKE long backup passphrase ñ 🔐";

    [Fact]
    public void Transfers_all_secrets_between_independent_stores_without_printing_them()
    {
        using var source = new CliHarness();
        using var destination = new CliHarness();
        string secret = Fake.Secret();
        source.Store.Set("openai", secret);
        source.Store.Set("github", Fake.Secret());
        string file = Path.Combine(source.WorkingDirectory, "portable backup.agentsecrets-backup");

        Assert.Equal(0, Interactive(source, "export", file));
        Assert.Equal(0, Interactive(destination, "import", file));
        Assert.Equal(secret, destination.Store.Get("openai"));
        Assert.Equal(source.Store.List(), destination.Store.List());
        Assert.True(source.Store.Exists("openai"));
        Assert.DoesNotContain(secret, Encoding.UTF8.GetString(File.ReadAllBytes(file)));
        Assert.DoesNotContain(secret, source.AllOutput + destination.AllOutput);
        Assert.DoesNotContain(Password, source.AllOutput + destination.AllOutput);
        Assert.Empty(Directory.GetFiles(source.WorkingDirectory, "*.tmp-*"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
    }

    [Theory]
    [InlineData("export")]
    [InlineData("import")]
    public void Refuses_noninteractive_use_before_reading_secrets_or_files(string command)
    {
        using var cli = new CliHarness();
        cli.Store.Set("openai", Fake.Secret());

        Assert.Equal(1, cli.Run([command, "backup.agentsecrets-backup"], stdin: Password, typedSecret: null));
        Assert.Contains("interactive terminal", cli.AllOutput);
        Assert.Equal(0, cli.Store.GetCalls);
        Assert.False(File.Exists(Path.Combine(cli.WorkingDirectory, "backup.agentsecrets-backup")));
        Assert.DoesNotContain(Password, cli.AllOutput);
    }

    [Fact]
    public void Rejects_password_arguments_without_echoing_them()
    {
        using var cli = new CliHarness();

        Assert.Equal(2, cli.Run("export", "backup", "--password", Password));
        Assert.Equal(2, cli.Run("import", "backup", Password));
        Assert.DoesNotContain(Password, cli.AllOutput);
        Assert.Equal(0, cli.Store.GetCalls);
    }

    [Fact]
    public void Export_never_overwrites_an_existing_file()
    {
        using var cli = new CliHarness();
        string file = Path.Combine(cli.WorkingDirectory, "existing");
        File.WriteAllText(file, "keep this file");
        cli.Store.Set("openai", Fake.Secret());

        Assert.Equal(1, cli.Run("export", file)); // No password prompt or value reads.
        Assert.Equal("keep this file", File.ReadAllText(file));
        Assert.Equal(0, cli.Store.GetCalls);
    }

    [Fact]
    public void Password_mismatch_creates_no_file_and_reads_no_values()
    {
        using var cli = new CliHarness();
        cli.Store.Set("openai", Fake.Secret());
        string file = Path.Combine(cli.WorkingDirectory, "backup");

        Assert.Equal(1, cli.Run(["export", file], stdin: null, typedSecret: null, typedSecrets: [Password, "different"]));
        Assert.False(File.Exists(file));
        Assert.Equal(0, cli.Store.GetCalls);
        Assert.DoesNotContain(Password, cli.AllOutput);
    }

    [Fact]
    public void Wrong_password_and_modified_file_leave_the_destination_unchanged()
    {
        using var cli = new CliHarness();
        string file = MakeBackup(cli, new() { ["openai"] = Fake.Secret() });
        cli.Store.Set("existing", "keep");

        Assert.Equal(1, cli.Run(["import", file], stdin: null, typedSecret: "wrong"));
        byte[] data = File.ReadAllBytes(file);
        data[^1] ^= 1;
        File.WriteAllBytes(file, data);
        Assert.Equal(1, Interactive(cli, "import", file));
        Assert.Equal(["existing"], cli.Store.List());
        Assert.Equal("keep", cli.Store.Get("existing"));
    }

    [Fact]
    public void Import_checks_all_conflicts_before_writing_and_replaces_only_with_force()
    {
        using var cli = new CliHarness();
        string secret = Fake.Secret();
        string file = MakeBackup(cli, new() { ["new-alias"] = Fake.Secret(), ["openai"] = secret });
        cli.Store.Set("openai", "old");

        Assert.Equal(1, Interactive(cli, "import", file));
        Assert.False(cli.Store.Exists("new-alias"));
        Assert.Equal("old", cli.Store.Get("openai"));
        Assert.Equal(0, Interactive(cli, "import", file, "--force"));
        Assert.Equal(secret, cli.Store.Get("openai"));
        Assert.True(cli.Store.Exists("new-alias"));
        Assert.DoesNotContain(secret, cli.AllOutput);
    }

    [PlatformFact(OS.Windows)]
    public void Import_preflights_windows_size_limit_before_writing_any_secret()
    {
        using var cli = new CliHarness();
        string file = MakeBackup(cli, new() { ["small"] = Fake.Secret(), ["large"] = new string('k', 1281) });

        Assert.Equal(1, Interactive(cli, "import", file));
        Assert.Empty(cli.Store.List());
        Assert.Contains("1280", cli.AllOutput);
    }

    [Fact]
    public void Reports_file_errors_without_crashing_or_revealing_passwords()
    {
        using var cli = new CliHarness();
        Assert.Equal(1, Interactive(cli, "import", "missing"));
        cli.Store.Set("openai", Fake.Secret());
        Assert.Equal(1, Interactive(cli, "export", Path.Combine(cli.WorkingDirectory, "missing-directory", "backup")));
        Assert.DoesNotContain(Password, cli.AllOutput);
    }

    [Fact]
    public void Rejects_oversized_files_before_prompting_for_a_password()
    {
        using var cli = new CliHarness();
        string file = Path.Combine(cli.WorkingDirectory, "oversized");
        using (var stream = File.Create(file))
        {
            stream.SetLength(EncryptedBackup.MaxFileBytes + 1);
        }

        Assert.Equal(1, cli.Run("import", file));
        Assert.Empty(cli.Store.List());
    }

    [Fact]
    public void Reports_partial_storage_failure_without_reflecting_backend_values()
    {
        using var cli = new CliHarness();
        string file = MakeBackup(cli, new() { ["first"] = Fake.Secret(), ["second"] = Fake.Secret() });
        string backendDetail = Fake.Secret();
        var context = new CliContext
        {
            Store = new FailingStore(cli.Store, backendDetail),
            Out = cli.Out,
            Error = cli.Error,
            In = new StringReader(""),
            InputRedirected = false,
            ReadSecretInteractive = () => Password,
            WorkingDirectory = cli.WorkingDirectory,
        };

        Assert.Equal(1, CliApp.Run(["import", file], context));
        Assert.True(cli.Store.Exists("first"));
        Assert.False(cli.Store.Exists("second"));
        Assert.Contains("1 secret(s) were imported", cli.AllOutput);
        Assert.DoesNotContain(backendDetail, cli.AllOutput);
    }

    private static int Interactive(CliHarness cli, params string[] args) =>
        cli.Run(args, stdin: null, typedSecret: Password);

    private static string MakeBackup(CliHarness cli, Dictionary<string, string> secrets)
    {
        string path = Path.Combine(cli.WorkingDirectory, "backup.agentsecrets-backup");
        File.WriteAllBytes(path, EncryptedBackup.Encrypt(secrets, Password));
        return path;
    }

    private sealed class FailingStore(ISecretStore inner, string detail) : ISecretStore
    {
        private int _writes;
        public string BackendName => inner.BackendName;
        public string? Get(string name) => inner.Get(name);
        public bool Exists(string name) => inner.Exists(name);
        public bool Remove(string name) => inner.Remove(name);
        public IReadOnlyList<string> List() => inner.List();
        public void Set(string name, string value)
        {
            if (++_writes == 2)
            {
                throw new SecretStoreException(detail);
            }

            inner.Set(name, value);
        }
    }
}
