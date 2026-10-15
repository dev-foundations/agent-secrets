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
            if (executable is null || IsSameFile(onPath, executable))
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
            string? directory = Path.GetDirectoryName(executable);
            if (OperatingSystem.IsWindows() && IsOnPersistedUserPath(directory))
            {
                report.Info("'agent-secrets' is on your user PATH, but this terminal was opened before that change. Open a new terminal.");
            }
            else if (OperatingSystem.IsWindows())
            {
                report.Problem($"'agent-secrets' is not on PATH. Reinstall with:  {CliApp.InstallCommand}  (then open a new terminal)");
            }
            else
            {
                // There is no persisted user PATH to compare with: shells build PATH from their startup files.
                report.Problem($"'agent-secrets' is not on PATH. If you just installed it, open a new terminal; otherwise add " +
                               $"{directory ?? "its directory"} to PATH in your shell profile, or reinstall with:  {CliApp.InstallCommand}");
            }
        }
    }

    private static bool IsSameFile(string path, string other) =>
        string.Equals(ResolveLinks(path), ResolveLinks(other), StringComparison.OrdinalIgnoreCase);

    // A symbolic link on PATH (for example one created by a package manager) still points at this copy.
    private static string ResolveLinks(string path)
    {
        string fullPath = Path.GetFullPath(path);
        try
        {
            return File.ResolveLinkTarget(fullPath, returnFinalTarget: true)?.FullName ?? fullPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return fullPath;
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

        if (OperatingSystem.IsMacOS() && store is MacKeychainStore keychain && keychain.IsDefaultKeychainUnlocked() == false)
        {
            report.Info("The login keychain is locked. 'run' asks macOS to unlock it; without a desktop session (SSH), " +
                        "unlock it first with: security unlock-keychain");
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
        string claudePath = Path.Combine(home, ".claude", "skills", "agent-secrets", "SKILL.md");
        if (File.Exists(claudePath))
        {
            report.Ok($"Claude Code: {claudePath}");
        }
        else
        {
            report.Info($"Claude Code: not installed (expected at {claudePath}; the installer adds it:  {CliApp.InstallCommand})");
        }

        // Codex scans ~/.agents/skills (its documented user-level location) and $CODEX_HOME/skills
        // (default ~/.codex/skills), without de-duplicating between them.
        string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } custom
            ? custom
            : Path.Combine(home, ".codex");
        string agentsPath = Path.Combine(home, ".agents", "skills", "agent-secrets", "SKILL.md");
        string codexHomePath = Path.Combine(codexHome, "skills", "agent-secrets", "SKILL.md");
        bool inAgents = File.Exists(agentsPath);
        bool inCodexHome = File.Exists(codexHomePath);

        if (inAgents && inCodexHome)
        {
            report.Problem($"Codex: installed twice, so Codex lists the skill twice. Keep one and delete the other:{Environment.NewLine}" +
                           $"         {agentsPath}{Environment.NewLine}         {codexHomePath}");
        }
        else if (inAgents)
        {
            report.Ok($"Codex: {agentsPath}");
        }
        else if (inCodexHome)
        {
            report.Ok($"Codex: {codexHomePath}   (CODEX_HOME location)");
        }
        else
        {
            report.Info($"Codex: not installed (expected at {agentsPath}; the installer adds it:  {CliApp.InstallCommand})");
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
