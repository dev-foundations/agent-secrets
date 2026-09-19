namespace AgentSecrets.Cli;

/// <summary>Everything a command needs from the outside world, so tests can substitute it.</summary>
public sealed class CliContext
{
    public required ISecretStore Store { get; init; }
    public required TextWriter Out { get; init; }
    public required TextWriter Error { get; init; }
    public required TextReader In { get; init; }

    /// <summary>True when stdin is not a terminal (piped, or run by a script or agent).</summary>
    public required bool InputRedirected { get; init; }

    /// <summary>Reads a secret from the terminal without echoing it.</summary>
    public required Func<string> ReadSecretInteractive { get; init; }

    public required string WorkingDirectory { get; init; }
}

public static class ExitCodes
{
    public const int Success = 0;

    /// <summary>General failure; also "secret does not exist" for 'exists', and failed checks for 'status'/'doctor'.</summary>
    public const int Error = 1;

    public const int Usage = 2;

    /// <summary>'run' failed before the child started (missing secret, invalid manifest, ...).</summary>
    public const int RunFailed = 125;

    /// <summary>'run': the command was found but cannot be executed.</summary>
    public const int CommandNotExecutable = 126;

    /// <summary>'run': the command was not found.</summary>
    public const int CommandNotFound = 127;
}
