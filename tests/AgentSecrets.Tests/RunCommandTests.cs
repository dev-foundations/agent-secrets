using AgentSecrets.Cli;

namespace AgentSecrets.Tests;

/// <summary>
/// 'run' against the fake store, starting a real child process (tests/AgentSecrets.TestProbe).
/// The probe verifies values by hash, so no test ever prints a secret.
/// </summary>
public class RunCommandTests
{
    [Fact]
    public void Injects_the_secret_into_the_child_process()
    {
        using var cli = new CliHarness();
        string secret = Fake.Secret();
        cli.Store.Set("openai", secret);

        int exit = cli.Run("run", "--env", "OPENAI_API_KEY=openai", "--", TestPaths.Probe, "env-matches", "OPENAI_API_KEY", Fake.Sha256(secret));

        Assert.Equal(0, exit);
        Assert.DoesNotContain(secret, cli.AllOutput);
    }

    [Fact]
    public void Injects_multiple_bindings()
    {
        using var cli = new CliHarness();
        cli.Store.Set("openai", Fake.Secret());
        cli.Store.Set("elevenlabs", Fake.Secret());

        int exit = cli.Run(
            "run", "--env", "OPENAI_API_KEY=openai", "-e", "ELEVENLABS_API_KEY=elevenlabs",
            "--", TestPaths.Probe, "env-check", "OPENAI_API_KEY", "ELEVENLABS_API_KEY");

        Assert.Equal(0, exit);
    }

    [Fact]
    public void Child_without_bindings_for_a_variable_does_not_see_it()
    {
        using var cli = new CliHarness();
        cli.Store.Set("openai", Fake.Secret());
        cli.Store.Set("elevenlabs", Fake.Secret());

        int exit = cli.Run("run", "--env", "OPENAI_API_KEY=openai", "--", TestPaths.Probe, "env-check", "AGENTSECRETS_TEST_UNBOUND_VARIABLE");

        Assert.Equal(1, exit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(42)]
    public void Preserves_the_child_exit_code(int code)
    {
        using var cli = new CliHarness();
        cli.Store.Set("openai", Fake.Secret());

        int exit = cli.Run("run", "--env", "OPENAI_API_KEY=openai", "--", TestPaths.Probe, "exit", code.ToString());

        Assert.Equal(code, exit);
    }

    [Fact]
    public void Does_not_modify_the_parent_environment()
    {
        using var cli = new CliHarness();
        string variable = $"AGENTSECRETS_TEST_{Guid.NewGuid():N}";
        cli.Store.Set("openai", Fake.Secret());

        int exit = cli.Run("run", "--env", $"{variable}=openai", "--", TestPaths.Probe, "env-check", variable);

        Assert.Equal(0, exit); // the child had it...
        Assert.Null(Environment.GetEnvironmentVariable(variable)); // ...this process never did.
        Assert.Null(Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.User));
    }

    [Fact]
    public void Uses_bindings_from_the_manifest()
    {
        using var cli = new CliHarness();
        string secret = Fake.Secret();
        cli.Store.Set("openai", secret);
        cli.WriteManifest("""{ "version": 1, "bindings": { "OPENAI_API_KEY": "openai" } }""");

        int exit = cli.Run("run", "--", TestPaths.Probe, "env-matches", "OPENAI_API_KEY", Fake.Sha256(secret));

        Assert.Equal(0, exit);
    }

    [Fact]
    public void Explicit_binding_overrides_the_manifest_and_other_manifest_bindings_remain()
    {
        using var cli = new CliHarness();
        string personal = Fake.Secret();
        string work = Fake.Secret();
        string eleven = Fake.Secret();
        cli.Store.Set("openai", personal);
        cli.Store.Set("openai-work", work);
        cli.Store.Set("elevenlabs", eleven);
        cli.WriteManifest("""{ "version": 1, "bindings": { "OPENAI_API_KEY": "openai", "ELEVENLABS_API_KEY": "elevenlabs" } }""");

        Assert.Equal(0, cli.Run("run", "--env", "OPENAI_API_KEY=openai-work", "--", TestPaths.Probe, "env-matches", "OPENAI_API_KEY", Fake.Sha256(work)));
        Assert.Equal(0, cli.Run("run", "--env", "OPENAI_API_KEY=openai-work", "--", TestPaths.Probe, "env-matches", "ELEVENLABS_API_KEY", Fake.Sha256(eleven)));
    }

    [Fact]
    public void No_manifest_option_ignores_the_manifest()
    {
        using var cli = new CliHarness();
        cli.Store.Set("openai", Fake.Secret());
        cli.Store.Set("other", Fake.Secret());
        cli.WriteManifest("""{ "version": 1, "bindings": { "OPENAI_API_KEY": "openai" } }""");

        int exit = cli.Run("run", "--no-manifest", "--env", "OTHER_KEY=other", "--", TestPaths.Probe, "env-check", "OPENAI_API_KEY");

        Assert.Equal(1, exit);
    }

    [Fact]
    public void Missing_secret_fails_before_starting_the_child_and_explains_how_to_fix_it()
    {
        using var cli = new CliHarness();
        cli.Store.Set("openai", Fake.Secret());
        cli.WriteManifest("""{ "version": 1, "bindings": { "OPENAI_API_KEY": "openai", "ELEVENLABS_API_KEY": "elevenlabs" } }""");

        int exit = cli.Run("run", "--", TestPaths.Probe, "exit", "0");

        Assert.Equal(ExitCodes.RunFailed, exit);
        string error = cli.Error.ToString();
        Assert.Contains("Required secret 'elevenlabs' was not found", error);
        Assert.Contains("    agent-secrets set elevenlabs", error);
        Assert.DoesNotContain("agent-secrets set openai", error);
    }

    [Fact]
    public void Invalid_manifest_fails_with_a_clear_message()
    {
        using var cli = new CliHarness();
        cli.WriteManifest("""{ "version": 1, "bindings": { "OPENAI_API_KEY": 123 } }""");

        int exit = cli.Run("run", "--", TestPaths.Probe, "exit", "0");

        Assert.Equal(ExitCodes.RunFailed, exit);
        Assert.Contains("Invalid manifest", cli.Error.ToString());
    }

    [Fact]
    public void Nothing_to_inject_is_an_error()
    {
        using var cli = new CliHarness();

        Assert.Equal(ExitCodes.RunFailed, cli.Run("run", "--", TestPaths.Probe, "exit", "0"));
        Assert.Contains("Nothing to inject", cli.Error.ToString());
    }

    [Fact]
    public void Unknown_command_returns_127_without_reading_any_secret()
    {
        using var cli = new CliHarness();
        cli.Store.Set("openai", Fake.Secret());

        int exit = cli.Run("run", "--env", "OPENAI_API_KEY=openai", "--", "agentsecrets-no-such-program-xyz");

        Assert.Equal(ExitCodes.CommandNotFound, exit);
        Assert.Equal(0, cli.Store.GetCalls);
    }

    [Fact]
    public void A_secret_passed_where_a_name_belongs_is_not_echoed()
    {
        using var cli = new CliHarness();
        string pasted = Fake.Secret() + "+/==";

        int exit = cli.Run("run", "--env", $"OPENAI_API_KEY={pasted}", "--", TestPaths.Probe, "exit", "0");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.DoesNotContain(pasted, cli.AllOutput);
    }

    [Fact]
    public void Options_after_the_separator_belong_to_the_child()
    {
        using var cli = new CliHarness();
        cli.Store.Set("openai", Fake.Secret());

        // "--env" after "--" must be passed through, not interpreted (the probe then exits 64: unknown mode).
        int exit = cli.Run("run", "--env", "OPENAI_API_KEY=openai", "--", TestPaths.Probe, "--env", "X=y");

        Assert.Equal(64, exit);
    }

    [Fact]
    public void Usage_errors()
    {
        using var cli = new CliHarness();

        Assert.Equal(ExitCodes.Usage, cli.Run("run"));
        Assert.Equal(ExitCodes.Usage, cli.Run("run", "--env"));
        Assert.Equal(ExitCodes.Usage, cli.Run("run", "--bogus", "--", "cmd"));
        Assert.Equal(ExitCodes.Usage, cli.Run("run", "--env", "NOEQUALS", "--", "cmd"));
    }
}
