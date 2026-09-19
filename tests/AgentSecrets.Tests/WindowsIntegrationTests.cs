using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;

namespace AgentSecrets.Tests;

/// <summary>
/// Tests against the real Windows Credential Manager, using fake values only.
/// Store-level tests use their own target prefix, so they can never touch real AgentSecrets entries.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStoreTests : IDisposable
{
    private readonly WindowsCredentialStore _store = new($"AgentSecrets.Tests/{Guid.NewGuid():N}/");

    public void Dispose()
    {
        foreach (string name in _store.List())
        {
            _store.Remove(name);
        }
    }

    [Fact]
    public void Round_trips_a_secret()
    {
        string secret = Fake.Secret() + " ñ✓ with spaces";

        Assert.False(_store.Exists("openai"));
        Assert.Null(_store.Get("openai"));

        _store.Set("openai", secret);

        Assert.True(_store.Exists("openai"));
        Assert.Equal(secret, _store.Get("openai"));
    }

    [Fact]
    public void Overwrites_lists_and_removes()
    {
        _store.Set("openai", "first");
        _store.Set("openai", "second");
        _store.Set("elevenlabs", "x");

        Assert.Equal("second", _store.Get("openai"));
        Assert.Equal(["elevenlabs", "openai"], _store.List());

        Assert.True(_store.Remove("openai"));
        Assert.False(_store.Remove("openai"));
        Assert.Equal(["elevenlabs"], _store.List());
    }

    [Fact]
    public void Exists_matches_exact_names_only()
    {
        _store.Set("openai-work", "x");

        Assert.False(_store.Exists("openai"));
        Assert.True(_store.Exists("openai-work"));
    }

    [Fact]
    public void Rejects_values_that_exceed_the_credential_manager_limit_without_echoing_them()
    {
        string tooLong = new('k', WindowsCredentialStore.MaxSecretChars + 1);

        var ex = Assert.Throws<SecretStoreException>(() => _store.Set("big", tooLong));

        Assert.DoesNotContain(tooLong, ex.Message);
        _store.Set("big", new string('k', WindowsCredentialStore.MaxSecretChars)); // exactly at the limit is fine
    }
}

/// <summary>
/// End-to-end: the real agent-secrets.exe, the real Credential Manager, a real child process.
/// Uses a unique throw-away alias and removes it afterwards.
/// </summary>
public sealed class EndToEndTests : IDisposable
{
    private readonly string _alias = $"zz-e2e-test-{Guid.NewGuid():N}"[..28];
    private readonly string _secret = Fake.Secret();
    private readonly string _workDir = Directory.CreateTempSubdirectory("agentsecrets-e2e-").FullName;

    public EndToEndTests()
    {
        var set = RunCli(["set", _alias, "--stdin"], stdin: _secret + Environment.NewLine);
        Assert.True(set.ExitCode == 0, set.Output);
    }

    public void Dispose()
    {
        RunCli(["remove", _alias, "--yes"]);
        Directory.Delete(_workDir, recursive: true);
    }

    [Fact]
    public void Full_workflow_never_reveals_the_secret()
    {
        var transcript = new List<string>();

        var exists = RunCli(["exists", _alias]);
        Assert.Equal(0, exists.ExitCode);
        transcript.Add(exists.Output);

        var list = RunCli(["list"]);
        Assert.Contains(_alias, list.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        transcript.Add(list.Output);

        var run = RunCli(["run", "--env", $"DEMO_API_KEY={_alias}", "--", TestPaths.Probe, "env-check", "DEMO_API_KEY"]);
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("DEMO_API_KEY available: yes", run.Output);
        transcript.Add(run.Output);

        var verify = RunCli(["run", "--env", $"DEMO_API_KEY={_alias}", "--", TestPaths.Probe, "env-matches", "DEMO_API_KEY", Fake.Sha256(_secret)]);
        Assert.Equal(0, verify.ExitCode);
        transcript.Add(verify.Output);

        File.WriteAllText(Path.Combine(_workDir, Manifest.FileName), $$"""{ "version": 1, "bindings": { "DEMO_API_KEY": "{{_alias}}" } }""");
        var viaManifest = RunCli(["run", "--", TestPaths.Probe, "env-check", "DEMO_API_KEY"]);
        Assert.Equal(0, viaManifest.ExitCode);
        transcript.Add(viaManifest.Output);

        var status = RunCli(["status"]);
        Assert.Equal(0, status.ExitCode);
        Assert.Contains($"DEMO_API_KEY <- {_alias}", status.Output);
        transcript.Add(status.Output);

        var doctor = RunCli(["doctor"]);
        Assert.Contains("Windows Credential Manager is accessible", doctor.Output);
        transcript.Add(doctor.Output);

        var removed = RunCli(["remove", _alias, "--yes"]);
        Assert.Equal(0, removed.ExitCode);
        Assert.Equal(1, RunCli(["exists", _alias, "--quiet"]).ExitCode);

        var missing = RunCli(["run", "--", TestPaths.Probe, "env-check", "DEMO_API_KEY"]);
        Assert.Equal(125, missing.ExitCode);
        Assert.Contains($"agent-secrets set {_alias}", missing.Output);
        transcript.Add(missing.Output);

        Assert.DoesNotContain(_secret, string.Join("\n", transcript));
    }

    [Fact]
    public void Preserves_exit_code_and_stdio()
    {
        Assert.Equal(37, RunCli(["run", "--env", $"K={_alias}", "--", TestPaths.Probe, "exit", "37"]).ExitCode);

        var piped = RunCli(["run", "--env", $"K={_alias}", "--", TestPaths.Probe, "stdin-upper"], stdin: "hello from stdin");
        Assert.Equal(0, piped.ExitCode);
        Assert.Contains("HELLO FROM STDIN", piped.Output);
    }

    public static TheoryData<string[]> TrickyArguments => new()
    {
        new[] { "plain", "two words", "", "trailing\\", "C:\\Program Files\\x\\", "quote\"inside", "a\\\"b" },
        new[] { "%PATH%", "%USERNAME%", "100%", "a&b", "a|b", "<in>", "^caret", "(paren)", "semi;colon", "!bang!", "é ñ 日本" },
        new[] { "--flag=value with space", "-x", "--", "--env", "A=b", "'single'", "`tick`", "$var", "*.txt" },
    };

    [Theory]
    [MemberData(nameof(TrickyArguments))]
    public void Arguments_reach_an_executable_unchanged(string[] arguments)
    {
        var result = RunCli(["run", "--env", $"K={_alias}", "--", TestPaths.Probe, "args", .. arguments]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(arguments, ParseProbeArgs(result.Output));
    }

    [Theory]
    [MemberData(nameof(TrickyArguments))]
    public void Arguments_reach_a_batch_file_shim_unchanged(string[] arguments)
    {
        // The same shape as npm.cmd and similar shims: a batch file that forwards %* to an executable.
        string shim = Path.Combine(_workDir, "shim.cmd");
        File.WriteAllText(shim, $"@echo off\r\n\"{TestPaths.Probe}\" args %*\r\n");

        var result = RunCli(["run", "--env", $"K={_alias}", "--", shim, .. arguments]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(arguments, ParseProbeArgs(result.Output));
    }

    [Fact]
    public void Set_without_a_terminal_is_refused()
    {
        var result = RunCli(["set", _alias + "x"], stdin: Fake.Secret());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("interactive terminal", result.Output);
        Assert.Equal(1, RunCli(["exists", _alias + "x", "-q"]).ExitCode);
    }

    private static string[] ParseProbeArgs(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<string>(line)!)
            .ToArray();

    private (int ExitCode, string Output) RunCli(string[] args, string? stdin = null)
    {
        var startInfo = new ProcessStartInfo(TestPaths.Cli)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _workDir,
        };
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(startInfo)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
        }

        process.StandardInput.Close();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result + stderr.Result);
    }
}
