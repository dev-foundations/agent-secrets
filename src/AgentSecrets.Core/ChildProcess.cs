using System.ComponentModel;
using System.Diagnostics;

namespace AgentSecrets;

/// <summary>
/// Starts the child process with extra environment variables. The variables exist only in
/// the child's environment block; this process's own environment is never modified.
/// stdin/stdout/stderr are inherited, so interactive programs and pipes keep working.
/// </summary>
public static class ChildProcess
{
    public static ProcessStartInfo CreateStartInfo(
        string resolvedPath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };

        if (CommandResolver.IsBatchFile(resolvedPath))
        {
            startInfo.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            startInfo.Arguments = BatchCommandLine.Build(resolvedPath, arguments);
        }
        else
        {
            startInfo.FileName = resolvedPath;
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument); // .NET applies correct Windows quoting.
            }
        }

        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        return startInfo;
    }

    /// <summary>Runs the process to completion and returns its exit code.</summary>
    public static int Run(ProcessStartInfo startInfo)
    {
        // Ctrl+C reaches the whole console process group. Let the child decide how to
        // handle it and keep waiting, so its real exit code is what we return.
        ConsoleCancelEventHandler ignoreCtrlC = (_, e) => e.Cancel = true;
        Console.CancelKeyPress += ignoreCtrlC;
        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new ChildProcessException("The process could not be started.");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex)
        {
            // Only the native error code is reported; the message never includes the environment.
            throw new ChildProcessException($"The process could not be started (Win32 error {ex.NativeErrorCode}).");
        }
        finally
        {
            Console.CancelKeyPress -= ignoreCtrlC;
        }
    }
}

public sealed class ChildProcessException(string message) : Exception(message);
