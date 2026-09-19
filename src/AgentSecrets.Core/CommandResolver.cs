namespace AgentSecrets;

/// <summary>
/// Resolves a command name to a full path the way a Windows shell would (PATH + PATHEXT),
/// with two deliberate differences:
///  - the current directory is NOT searched for bare names (use ".\tool" explicitly), so a
///    repository cannot hijack "python" by shipping a "python.cmd";
///  - only .exe/.com/.cmd/.bat are accepted, because those are what we can start safely.
/// </summary>
public static class CommandResolver
{
    private static readonly string[] RunnableExtensions = [".exe", ".com", ".cmd", ".bat"];

    public static bool IsBatchFile(string path) =>
        System.IO.Path.GetExtension(path) is var ext
        && (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase));

    public static string Resolve(string command, string workingDirectory, string? pathVariable = null, string? pathExtVariable = null)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Contains('\0'))
        {
            throw new CommandResolutionException("The command to run is empty or invalid.", notFound: true);
        }

        pathVariable ??= Environment.GetEnvironmentVariable("PATH") ?? "";
        string[] extensions = GetExtensions(pathExtVariable ?? Environment.GetEnvironmentVariable("PATHEXT"));

        string? notRunnable = null;
        bool hasDirectory = command.Contains('\\') || command.Contains('/') || command.Contains(':');
        if (hasDirectory)
        {
            string basePath = System.IO.Path.GetFullPath(command, workingDirectory);
            if (TryCandidates(basePath, extensions, ref notRunnable) is { } found)
            {
                return found;
            }
        }
        else
        {
            foreach (string entry in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string directory = entry.Trim('"');
                if (directory.Length == 0 || !System.IO.Path.IsPathFullyQualified(directory))
                {
                    continue; // Relative PATH entries would re-introduce current-directory lookups.
                }

                if (TryCandidates(System.IO.Path.Combine(directory, command), extensions, ref notRunnable) is { } found)
                {
                    return found;
                }
            }
        }

        if (notRunnable is not null)
        {
            throw new CommandResolutionException(
                $"'{notRunnable}' is not an executable (.exe, .com, .cmd, .bat). Run it through its interpreter, e.g.: agent-secrets run -- python script.py   or   agent-secrets run -- pwsh -File script.ps1",
                notFound: false);
        }

        string hint = hasDirectory ? "" : " It was looked up on PATH (the current directory is not searched; use .\\name for a local program).";
        throw new CommandResolutionException($"Command '{command}' was not found.{hint}", notFound: true);
    }

    private static string? TryCandidates(string basePath, string[] extensions, ref string? notRunnable)
    {
        string existingExtension = System.IO.Path.GetExtension(basePath);
        if (existingExtension.Length > 0 && File.Exists(basePath))
        {
            if (RunnableExtensions.Contains(existingExtension, StringComparer.OrdinalIgnoreCase))
            {
                return basePath;
            }

            notRunnable ??= basePath;
        }

        foreach (string extension in extensions)
        {
            if (File.Exists(basePath + extension))
            {
                return basePath + extension;
            }
        }

        return null;
    }

    private static string[] GetExtensions(string? pathExt)
    {
        string[] fromEnvironment = (pathExt ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(ext => RunnableExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            .Select(ext => ext.ToLowerInvariant())
            .Distinct()
            .ToArray();
        return fromEnvironment.Length > 0 ? fromEnvironment : RunnableExtensions;
    }
}

public sealed class CommandResolutionException(string message, bool notFound) : Exception(message)
{
    /// <summary>True: nothing matched (exit 127). False: found but cannot be executed (exit 126).</summary>
    public bool NotFound { get; } = notFound;
}
