namespace AgentSecrets.Cli;

public static class SecretCommands
{
    public static int Set(string[] args, CliContext context)
    {
        var (name, flags) = ParseNameAndFlags(args, "set", "--force", "--stdin");
        bool force = flags.Contains("--force");
        bool fromStdin = flags.Contains("--stdin");

        if (context.InputRedirected && !fromStdin)
        {
            context.Error.WriteLine(
                $"agent-secrets: 'set' must be run by a person in an interactive terminal, so the secret is typed or pasted without being echoed or logged.");
            context.Error.WriteLine($"If you are a coding agent: do not try to supply the secret. Ask the user to run:  agent-secrets set {name}");
            return ExitCodes.Error;
        }

        if (context.Store.Exists(name) && !force)
        {
            if (fromStdin)
            {
                context.Error.WriteLine($"agent-secrets: secret '{name}' already exists. Use --force to overwrite it.");
                return ExitCodes.Error;
            }

            if (!Confirm(context, $"Secret '{name}' already exists. Overwrite it? [y/N] "))
            {
                context.Out.WriteLine("Nothing changed.");
                return ExitCodes.Error;
            }
        }

        string value;
        if (fromStdin)
        {
            value = context.In.ReadLine() ?? "";
        }
        else
        {
            context.Out.Write($"Enter the secret for '{name}' (input is hidden; paste is fine), then press Enter: ");
            value = context.ReadSecretInteractive();
            context.Out.WriteLine();
        }

        // Keys are frequently pasted with stray whitespace; no real credential starts or ends with it.
        value = value.Trim();
        if (value.Length == 0)
        {
            context.Error.WriteLine("agent-secrets: no secret was entered. Nothing was stored.");
            return ExitCodes.Error;
        }

        context.Store.Set(name, value);
        context.Out.WriteLine($"Secret '{name}' stored successfully.");
        return ExitCodes.Success;
    }

    public static int List(string[] args, CliContext context)
    {
        if (args.Length > 0)
        {
            throw new UsageException("Usage: agent-secrets list");
        }

        IReadOnlyList<string> names = context.Store.List();
        foreach (string name in names)
        {
            context.Out.WriteLine(name);
        }

        if (names.Count == 0)
        {
            // stderr, so scripts reading stdout simply see an empty list.
            context.Error.WriteLine("No secrets stored yet. Add one with: agent-secrets set <name>");
        }

        return ExitCodes.Success;
    }

    public static int Exists(string[] args, CliContext context)
    {
        var (name, flags) = ParseNameAndFlags(args, "exists", "--quiet", "-q");
        bool exists = context.Store.Exists(name);
        if (flags.Count == 0)
        {
            context.Out.WriteLine(exists ? $"Secret '{name}' exists." : $"Secret '{name}' was not found. Add it with: agent-secrets set {name}");
        }

        return exists ? ExitCodes.Success : ExitCodes.Error;
    }

    public static int Remove(string[] args, CliContext context)
    {
        var (name, flags) = ParseNameAndFlags(args, "remove", "--yes", "-y");
        if (!context.Store.Exists(name))
        {
            context.Error.WriteLine($"agent-secrets: secret '{name}' was not found.");
            return ExitCodes.Error;
        }

        if (flags.Count == 0)
        {
            if (context.InputRedirected)
            {
                context.Error.WriteLine("agent-secrets: refusing to remove without confirmation. Pass --yes to remove non-interactively.");
                return ExitCodes.Error;
            }

            if (!Confirm(context, $"Remove secret '{name}'? This cannot be undone. [y/N] "))
            {
                context.Out.WriteLine("Nothing changed.");
                return ExitCodes.Error;
            }
        }

        context.Store.Remove(name);
        context.Out.WriteLine($"Secret '{name}' removed.");
        return ExitCodes.Success;
    }

    private static bool Confirm(CliContext context, string prompt)
    {
        context.Out.Write(prompt);
        string answer = (context.In.ReadLine() ?? "").Trim();
        return answer.Equals("y", StringComparison.OrdinalIgnoreCase) || answer.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Name, HashSet<string> Flags) ParseNameAndFlags(string[] args, string command, params string[] allowedFlags)
    {
        string? name = null;
        var flags = new HashSet<string>();
        foreach (string arg in args)
        {
            if (arg.StartsWith('-'))
            {
                if (!allowedFlags.Contains(arg))
                {
                    throw new UsageException($"Unknown option '{arg}' for '{command}'.");
                }

                flags.Add(arg);
            }
            else if (name is null)
            {
                name = arg;
            }
            else
            {
                // Most likely someone tried "set <name> <value>". Never echo the extra argument.
                throw new UsageException(command == "set"
                    ? "'set' takes only a name. The secret value is never passed on the command line (it would end up in shell history); you will be prompted for it."
                    : $"'{command}' takes exactly one secret name.");
            }
        }

        if (name is null)
        {
            throw new UsageException($"Usage: agent-secrets {command} <name>");
        }

        return (SecretName.Normalize(name), flags);
    }
}
