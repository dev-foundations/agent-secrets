using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AgentSecrets;

/// <summary>
/// Starts the child process with extra environment variables. The variables exist only in
/// the child's environment block; this process's own environment is never modified.
/// stdin/stdout/stderr are inherited, so interactive programs and pipes keep working.
/// </summary>
public static partial class ChildProcess
{
    private const int SIGTERM = 15; // the same number on macOS and Linux

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

        if (OperatingSystem.IsWindows() && CommandResolver.IsBatchFile(resolvedPath))
        {
            startInfo.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            startInfo.Arguments = BatchCommandLine.Build(resolvedPath, arguments);
        }
        else
        {
            startInfo.FileName = resolvedPath;
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument); // .NET applies correct Windows quoting; on Unix this is argv as-is.
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

        // A SIGTERM sent to agent-secrets alone (for example by a tool's timeout) is passed on to the
        // child instead of leaving it running on its own, with the secrets. As with Ctrl+C we keep
        // waiting, so the child's exit code is still what we return. One that arrives while the
        // child is being started is remembered and passed on as soon as the child's id is known.
        int childId = 0, terminationRequested = 0;
        PosixSignalRegistration? forwardSigterm = OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()
            ? PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                Interlocked.Exchange(ref terminationRequested, 1); // a full fence, as below:
                int id = Volatile.Read(ref childId);                // one side always sees the other
                if (id > 0)
                {
                    _ = kill(id, SIGTERM);
                }
            })
            : null;
        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new ChildProcessException("The process could not be started.");
            Interlocked.Exchange(ref childId, process.Id);
            if (Volatile.Read(ref terminationRequested) == 1)
            {
                _ = kill(process.Id, SIGTERM);
            }

            process.WaitForExit();
            Interlocked.Exchange(ref childId, 0); // The id is free for reuse once the child is gone.
            return process.ExitCode;
        }
        catch (Win32Exception ex)
        {
            // Only the native error code is reported; the message never includes the environment.
            throw new ChildProcessException($"The process could not be started ({DescribeStartError(ex.NativeErrorCode)}).");
        }
        finally
        {
            forwardSigterm?.Dispose();
            Console.CancelKeyPress -= ignoreCtrlC;
        }
    }

    [LibraryImport("libc")]
    private static partial int kill(int pid, int signal);

    private static string DescribeStartError(int code)
    {
        const int ENOEXEC = 8; // the same number on macOS and Linux
        if (OperatingSystem.IsWindows())
        {
            return $"Win32 error {code}";
        }

        return code == ENOEXEC
            ? $"errno {code}: it is neither a program nor a script starting with '#!'; run it through its interpreter, e.g. -- sh script.sh"
            : $"errno {code}";
    }
}

public sealed class ChildProcessException(string message) : Exception(message);
