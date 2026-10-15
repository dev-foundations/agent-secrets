using System.Reflection;

namespace AgentSecrets.Cli;

public static class CliApp
{
    public const string HelpText = """
        agent-secrets - store secrets once, refer to them by name, inject them only into child processes.

        Usage:
          agent-secrets set <name> [--force] [--stdin]    Store a secret (prompted, never echoed)
          agent-secrets list                              List secret names (never values)
          agent-secrets exists <name> [--quiet]           Exit code 0 if the secret exists, 1 if not
          agent-secrets remove <name> [--yes]             Delete a secret
          agent-secrets export <file>                    Export all secrets to a password-encrypted file
          agent-secrets import <file> [--force]           Import an encrypted backup (prompted password)
          agent-secrets run [options] -- <command> [args] Run a command with secrets in its environment
          agent-secrets status                            Check the current project's required secrets
          agent-secrets doctor                            Check installation, storage, project and agent skills

        Options for 'run':
          -e, --env NAME=secret   Inject <secret> as environment variable NAME (repeatable).
                                  Overrides a binding for NAME from the manifest.
          --manifest <path>       Use this manifest instead of searching for .agentsecrets.json
          --no-manifest           Ignore any .agentsecrets.json

        Examples:
          agent-secrets set openai
          agent-secrets run --env OPENAI_API_KEY=openai -- python demo.py
          agent-secrets run -- python app.py              (bindings from .agentsecrets.json)

        Project manifest (.agentsecrets.json, safe to commit - names only):
          { "version": 1, "bindings": { "OPENAI_API_KEY": "openai" } }

        There is intentionally no command that prints a secret value.
        """;

    public static string InstallCommand => OperatingSystem.IsWindows()
        ? "irm https://github.com/dev-foundations/agent-secrets/releases/latest/download/install.ps1 | iex"
        : "curl -fsSL https://github.com/dev-foundations/agent-secrets/releases/latest/download/install.sh | sh";

    public static string Version =>
        typeof(CliApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public static int Run(string[] args, CliContext context)
    {
        if (args.Length == 0)
        {
            context.Out.WriteLine(HelpText);
            return ExitCodes.Usage;
        }

        string command = args[0];
        string[] rest = args[1..];
        try
        {
            switch (command)
            {
                case "help" or "--help" or "-h" or "/?":
                    context.Out.WriteLine(HelpText);
                    return ExitCodes.Success;
                case "version" or "--version":
                    context.Out.WriteLine($"agent-secrets {Version}");
                    return ExitCodes.Success;
                case "set":
                    return SecretCommands.Set(rest, context);
                case "list" or "ls":
                    return SecretCommands.List(rest, context);
                case "exists":
                    return SecretCommands.Exists(rest, context);
                case "remove" or "rm" or "delete":
                    return SecretCommands.Remove(rest, context);
                case "export":
                    return BackupCommands.Export(rest, context);
                case "import":
                    return BackupCommands.Import(rest, context);
                case "run":
                    return RunCommand.Execute(rest, context);
                case "status":
                    return DiagnosticsCommand.Execute(rest, context, full: false);
                case "doctor":
                    return DiagnosticsCommand.Execute(rest, context, full: true);
                case "get" or "show" or "print" or "dump" or "reveal":
                    context.Error.WriteLine(
                        $"agent-secrets: there is intentionally no '{command}' command. Secrets are never printed; " +
                        "use an encrypted backup for transfer, or inject them into a child process:");
                    context.Error.WriteLine("    agent-secrets run --env NAME=<secret-name> -- <command>");
                    return ExitCodes.Usage;
                default:
                    throw new UsageException($"Unknown command '{command}'. Run 'agent-secrets help'.");
            }
        }
        catch (UsageException ex)
        {
            context.Error.WriteLine($"agent-secrets: {ex.Message}");
            return ExitCodes.Usage;
        }
        catch (ArgumentException ex)
        {
            // Validation errors from Core (names, bindings). Messages never contain secret values.
            context.Error.WriteLine($"agent-secrets: {ex.Message}");
            return ExitCodes.Usage;
        }
        catch (SecretStoreException ex)
        {
            context.Error.WriteLine($"agent-secrets: {ex.Message}");
            return command == "run" ? ExitCodes.RunFailed : ExitCodes.Error;
        }
        catch (BackupException ex)
        {
            context.Error.WriteLine($"agent-secrets: {ex.Message}");
            return ExitCodes.Error;
        }
        catch (Exception ex) when ((command is "export" or "import") && (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException))
        {
            // File and cryptography exceptions are not reflected: no payload or password in output.
            context.Error.WriteLine("agent-secrets: backup failed. Check the file path, permissions, available disk space and cryptography support.");
            return ExitCodes.Error;
        }
    }
}

public sealed class UsageException(string message) : Exception(message);
