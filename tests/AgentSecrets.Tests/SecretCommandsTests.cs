using AgentSecrets.Cli;

namespace AgentSecrets.Tests;

public class SecretCommandsTests
{
    [Fact]
    public void Set_interactive_stores_secret_and_never_prints_it()
    {
        using var cli = new CliHarness();
        string secret = Fake.Secret();

        int exit = cli.Run(["set", "openai"], stdin: null, typedSecret: secret);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(secret, cli.Store.Get("openai"));
        Assert.Contains("Secret 'openai' stored successfully.", cli.Out.ToString());
        Assert.DoesNotContain(secret, cli.AllOutput);
    }

    [Fact]
    public void Set_normalizes_name_and_trims_pasted_whitespace()
    {
        using var cli = new CliHarness();
        string secret = Fake.Secret();

        cli.Run(["set", "OpenAI"], stdin: null, typedSecret: $"  {secret}\t");

        Assert.Equal(secret, cli.Store.Get("openai"));
    }

    [Fact]
    public void Set_refuses_redirected_stdin_unless_stdin_flag_is_given()
    {
        using var cli = new CliHarness();
        string secret = Fake.Secret();

        int exit = cli.Run(["set", "openai"], stdin: secret, typedSecret: null);

        Assert.Equal(ExitCodes.Error, exit);
        Assert.False(cli.Store.Exists("openai"));
        Assert.Contains("agent-secrets set openai", cli.Error.ToString());
        Assert.DoesNotContain(secret, cli.AllOutput);
    }

    [Fact]
    public void Set_with_stdin_flag_reads_one_line()
    {
        using var cli = new CliHarness();
        string secret = Fake.Secret();

        int exit = cli.Run(["set", "openai", "--stdin"], stdin: secret + "\r\n", typedSecret: null);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(secret, cli.Store.Get("openai"));
        Assert.DoesNotContain(secret, cli.AllOutput);
    }

    [Fact]
    public void Set_never_accepts_the_value_as_an_argument_and_does_not_echo_it()
    {
        using var cli = new CliHarness();
        string secret = Fake.Secret();

        int exit = cli.Run(["set", "openai", secret], stdin: null, typedSecret: null);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.False(cli.Store.Exists("openai"));
        Assert.DoesNotContain(secret, cli.AllOutput);
    }

    [Fact]
    public void Set_invalid_name_is_rejected_without_echoing_it()
    {
        using var cli = new CliHarness();

        int exit = cli.Run(["set", "not/a valid*name"], stdin: null, typedSecret: Fake.Secret());

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.DoesNotContain("not/a valid*name", cli.AllOutput);
    }

    [Fact]
    public void Set_existing_secret_requires_confirmation_or_force()
    {
        using var cli = new CliHarness();
        cli.Store.Set("openai", "old");

        Assert.Equal(ExitCodes.Error, cli.Run(["set", "openai"], stdin: null, typedSecret: "new", answers: "n\n"));
        Assert.Equal("old", cli.Store.Get("openai"));

        Assert.Equal(ExitCodes.Success, cli.Run(["set", "openai"], stdin: null, typedSecret: "new", answers: "y\n"));
        Assert.Equal("new", cli.Store.Get("openai"));

        Assert.Equal(ExitCodes.Error, cli.Run(["set", "openai", "--stdin"], stdin: "newer", typedSecret: null));
        Assert.Equal(ExitCodes.Success, cli.Run(["set", "openai", "--stdin", "--force"], stdin: "newer", typedSecret: null));
        Assert.Equal("newer", cli.Store.Get("openai"));
    }

    [Fact]
    public void Set_empty_secret_is_rejected()
    {
        using var cli = new CliHarness();

        Assert.Equal(ExitCodes.Error, cli.Run(["set", "openai"], stdin: null, typedSecret: "   "));
        Assert.False(cli.Store.Exists("openai"));
    }

    [Fact]
    public void List_prints_sorted_names_only()
    {
        using var cli = new CliHarness();
        string secret = Fake.Secret();
        cli.Store.Set("openai", secret);
        cli.Store.Set("elevenlabs", secret);
        cli.Store.Set("github", secret);

        int exit = cli.Run("list");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["elevenlabs", "github", "openai"], cli.Out.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.DoesNotContain(secret, cli.AllOutput);
        Assert.Equal(0, cli.Store.GetCalls);
    }

    [Fact]
    public void Exists_reports_through_exit_code_without_reading_the_value()
    {
        using var cli = new CliHarness();
        cli.Store.Set("openai", Fake.Secret());

        Assert.Equal(ExitCodes.Success, cli.Run("exists", "openai"));
        Assert.Equal(ExitCodes.Success, cli.Run("exists", "OPENAI", "--quiet"));
        Assert.Equal(ExitCodes.Error, cli.Run("exists", "elevenlabs"));
        Assert.Contains("agent-secrets set elevenlabs", cli.Out.ToString());
        Assert.Equal(0, cli.Store.GetCalls);
    }

    [Fact]
    public void Remove_with_yes_deletes_the_secret()
    {
        using var cli = new CliHarness();
        cli.Store.Set("openai", Fake.Secret());

        Assert.Equal(ExitCodes.Success, cli.Run("remove", "openai", "--yes"));
        Assert.False(cli.Store.Exists("openai"));
        Assert.Equal(ExitCodes.Error, cli.Run("remove", "openai", "--yes"));
    }

    [Fact]
    public void Remove_asks_for_confirmation_interactively_and_refuses_when_not_interactive()
    {
        using var cli = new CliHarness();
        cli.Store.Set("openai", Fake.Secret());

        Assert.Equal(ExitCodes.Error, cli.Run(["remove", "openai"], stdin: null, typedSecret: null, answers: "n\n"));
        Assert.True(cli.Store.Exists("openai"));

        Assert.Equal(ExitCodes.Error, cli.Run(["remove", "openai"], stdin: "y\n", typedSecret: null));
        Assert.True(cli.Store.Exists("openai"));

        Assert.Equal(ExitCodes.Success, cli.Run(["remove", "openai"], stdin: null, typedSecret: null, answers: "y\n"));
        Assert.False(cli.Store.Exists("openai"));
    }

    [Theory]
    [InlineData("get")]
    [InlineData("export")]
    public void There_is_no_command_that_reveals_secrets(string command)
    {
        using var cli = new CliHarness();
        string secret = Fake.Secret();
        cli.Store.Set("openai", secret);

        int exit = cli.Run(command, "openai");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("intentionally", cli.Error.ToString());
        Assert.DoesNotContain(secret, cli.AllOutput);
        Assert.Equal(0, cli.Store.GetCalls);
    }
}
