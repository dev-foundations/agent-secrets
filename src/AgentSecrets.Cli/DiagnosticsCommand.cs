namespace AgentSecrets.Cli;

/// <summary>
/// 'status' (project-focused) and 'doctor' (everything). Neither ever retrieves a secret
/// value: they only ask the store whether names exist.
/// </summary>
public static class DiagnosticsCommand
{
    public static int Execute(string[] args, CliContext context, bool full)
    {
        if (args.Length > 0)
        {
            throw new UsageException($"Usage: agent-secrets {(full ? "doctor" : "status")}");
        }

        var report = new Report(context.Out);
        context.Out.WriteLine($"AgentSecrets {CliApp.Version}");

        if (full)
        {
            CheckInstallation(report);
        }

        CheckStore(report, context.Store);
        CheckProject(report, context);

        if (full)
        {
            CheckSkills(report);
        }

        context.Out.WriteLine();
        context.Out.WriteLine(report.Problems == 0 ? "No problems found." : $"{report.Problems} problem(s) found.");
        return report.Problems == 0 ? ExitCodes.Success : ExitCodes.Error;
    }

    private static void CheckInstallation(Report report)
    {
        report.Section("Installation");
        string? executable = Environment.ProcessPath;
        report.Ok($"Executable: {executable ?? "(unknown)"}");

        try
        {
            string onPath = CommandResolver.Resolve("agent-secrets", Environment.CurrentDirectory);
            if (executable is null || string.Equals(Path.GetFullPath(onPath), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase))
            {
                report.Ok("'agent-secrets' is on PATH");
            }
            else
            {
                report.Info($"'agent-secrets' on PATH is a different copy: {onPath}");
            }
        }
        catch (CommandResolutionException)
        {
            if (IsOnPersistedUserPath(Path.GetDirectoryName(executable)))
            {
                report.Info("'agent-secrets' is on your user PATH, but this terminal was opened before that change. Open a new terminal.");
            }
            else
            {
                report.Problem($"'agent-secrets' is not on PATH. Reinstall with:  {CliApp.InstallCommand}  (then open a new terminal)");
            }
        }
    }

    private static bool IsOnPersistedUserPath(string? directory)
    {
        if (directory is null)
        {
            return false;
        }

        string userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        return userPath
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(entry => string.Equals(entry.TrimEnd('\\'), directory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
    }

    private static void CheckStore(Report report, ISecretStore store)
    {
        report.Section("Secret storage");
        try
        {
            int count = store.List().Count;
            report.Ok($"{store.BackendName} is accessible ({count} secret{(count == 1 ? "" : "s")} stored)");
        }
        catch (SecretStoreException ex)
        {
            report.Problem(ex.Message);
        }
    }

    private static void CheckProject(Report report, CliContext context)
    {
        report.Section("Project");
        string? manifestPath = Manifest.Locate(context.WorkingDirectory);
        if (manifestPath is null)
        {
            report.Info($"No {Manifest.FileName} found for {context.WorkingDirectory} (optional; use --env bindings instead)");
            return;
        }

        Manifest manifest;
        try
        {
            manifest = Manifest.Load(manifestPath);
        }
        catch (ManifestException ex)
        {
            report.Problem(ex.Message);
            return;
        }

        report.Ok($"Manifest: {manifestPath}");
        if (manifest.Bindings.Count == 0)
        {
            report.Info("The manifest has no bindings");
        }

        foreach (var (envName, alias) in manifest.Bindings)
        {
            try
            {
                if (context.Store.Exists(alias))
                {
                    report.Ok($"  {envName} <- {alias}");
                }
                else
                {
                    report.Problem($"  {envName} <- {alias}   MISSING. Add it with: agent-secrets set {alias}");
                }
            }
            catch (SecretStoreException ex)
            {
                report.Problem($"  {envName} <- {alias}   {ex.Message}");
            }
        }
    }

    private static void CheckSkills(Report report)
    {
        report.Section("Agent skill (optional)");
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Codex reads $CODEX_HOME/skills, which defaults to ~/.codex/skills.
        string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } custom
            ? custom
            : Path.Combine(home, ".codex");
        (string Agent, string Path)[] locations =
        [
            ("Claude Code", Path.Combine(home, ".claude", "skills", "agent-secrets", "SKILL.md")),
            ("Codex", Path.Combine(codexHome, "skills", "agent-secrets", "SKILL.md")),
        ];

        foreach (var (agent, path) in locations)
        {
            if (File.Exists(path))
            {
                report.Ok($"{agent}: {path}");
            }
            else
            {
                report.Info($"{agent}: not installed (expected at {path}; the installer adds it:  {CliApp.InstallCommand})");
            }
        }

        // Installed by 'install.ps1 -AgentsSkills', or by a version before 0.1.1.
        string agentsPath = Path.Combine(home, ".agents", "skills", "agent-secrets", "SKILL.md");
        if (File.Exists(agentsPath))
        {
            report.Info($"Codex (.agents): {agentsPath}");
        }
    }

    private sealed class Report(TextWriter output)
    {
        public int Problems { get; private set; }

        public void Section(string title)
        {
            output.WriteLine();
            output.WriteLine(title);
        }

        public void Ok(string message) => output.WriteLine($"  [ok] {message}");

        public void Info(string message) => output.WriteLine($"  [--] {message}");

        public void Problem(string message)
        {
            Problems++;
            output.WriteLine($"  [!!] {message}");
        }
    }
}
