using System.Diagnostics;

namespace AgentSecrets.Cli;

public static class RunCommand
{
    private const string Usage = "Usage: agent-secrets run [--env NAME=secret]... [--manifest <path> | --no-manifest] -- <command> [args...]";

    public static int Execute(string[] args, CliContext context)
    {
        var explicitBindings = new List<KeyValuePair<string, string>>();
        string? manifestPath = null;
        bool noManifest = false;

        int i = 0;
        for (; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--")
            {
                i++;
                break;
            }

            if (arg is "--env" or "-e")
            {
                var (envName, alias) = Bindings.Parse(NextValue(args, ref i, arg));
                explicitBindings.Add(new(envName, alias));
            }
            else if (arg.StartsWith("--env=", StringComparison.Ordinal))
            {
                var (envName, alias) = Bindings.Parse(arg["--env=".Length..]);
                explicitBindings.Add(new(envName, alias));
            }
            else if (arg == "--manifest")
            {
                manifestPath = NextValue(args, ref i, arg);
            }
            else if (arg == "--no-manifest")
            {
                noManifest = true;
            }
            else if (arg.StartsWith('-'))
            {
                throw new UsageException($"Unknown option '{arg}' for 'run'. Put '--' before the command to run.\n{Usage}");
            }
            else
            {
                break; // First non-option token starts the command, so '--' is optional.
            }
        }

        string[] command = args[i..];
        if (command.Length == 0)
        {
            throw new UsageException($"No command to run.\n{Usage}");
        }

        if (noManifest && manifestPath is not null)
        {
            throw new UsageException("--manifest and --no-manifest cannot be combined.");
        }

        // 1. Work out the bindings (manifest first, explicit --env overrides).
        Manifest? manifest = null;
        try
        {
            if (manifestPath is not null)
            {
                string fullPath = Path.GetFullPath(manifestPath, context.WorkingDirectory);
                if (!File.Exists(fullPath))
                {
                    return Fail(context, ExitCodes.RunFailed, $"Manifest '{fullPath}' was not found.");
                }

                manifest = Manifest.Load(fullPath);
            }
            else if (!noManifest && Manifest.Locate(context.WorkingDirectory) is { } located)
            {
                manifest = Manifest.Load(located);
            }
        }
        catch (ManifestException ex)
        {
            return Fail(context, ExitCodes.RunFailed, ex.Message);
        }

        Dictionary<string, string> bindings = Bindings.Merge(manifest?.Bindings, explicitBindings);
        if (bindings.Count == 0)
        {
            return Fail(context, ExitCodes.RunFailed,
                $"Nothing to inject: no {Manifest.FileName} was found for '{context.WorkingDirectory}' and no --env binding was given.\n" +
                "Example: agent-secrets run --env OPENAI_API_KEY=openai -- python demo.py");
        }

        // 2. Resolve the command before touching any secret.
        string resolvedCommand;
        try
        {
            resolvedCommand = CommandResolver.Resolve(command[0], context.WorkingDirectory);
        }
        catch (CommandResolutionException ex)
        {
            return Fail(context, ex.NotFound ? ExitCodes.CommandNotFound : ExitCodes.CommandNotExecutable, ex.Message);
        }

        // 3. Retrieve the secrets. They go straight into the child's environment block.
        var environment = Bindings.Create();
        var missing = new List<KeyValuePair<string, string>>();
        foreach (var (envName, alias) in bindings)
        {
            if (context.Store.Get(alias) is { } value)
            {
                environment[envName] = value;
            }
            else
            {
                missing.Add(new(envName, alias));
            }
        }

        if (missing.Count > 0)
        {
            ReportMissing(context, missing);
            return ExitCodes.RunFailed;
        }

        // 4. Run the child and hand back its exit code.
        try
        {
            ProcessStartInfo startInfo = ChildProcess.CreateStartInfo(resolvedCommand, command[1..], environment, context.WorkingDirectory);
            return ChildProcess.Run(startInfo);
        }
        catch (ChildProcessException ex)
        {
            return Fail(context, ExitCodes.CommandNotExecutable, $"{ex.Message} Command: {resolvedCommand}");
        }
    }

    public static void ReportMissing(CliContext context, IReadOnlyList<KeyValuePair<string, string>> missing)
    {
        foreach (var (envName, alias) in missing)
        {
            context.Error.WriteLine($"Required secret '{alias}' was not found (needed for {envName}).");
        }

        context.Error.WriteLine();
        context.Error.WriteLine("Add it with:");
        context.Error.WriteLine();
        foreach (string alias in missing.Select(m => m.Value).Distinct())
        {
            context.Error.WriteLine($"    agent-secrets set {alias}");
        }

        context.Error.WriteLine();
        context.Error.WriteLine("This must be done by a person in their own terminal (the value is typed or pasted, hidden).");
        context.Error.WriteLine("Coding agents: ask the user to run the command above; do not look for the key elsewhere.");
    }

    private static string NextValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || args[index + 1] == "--")
        {
            throw new UsageException($"Option '{option}' needs a value.\n{Usage}");
        }

        return args[++index];
    }

    private static int Fail(CliContext context, int exitCode, string message)
    {
        context.Error.WriteLine($"agent-secrets: {message}");
        return exitCode;
    }
}
