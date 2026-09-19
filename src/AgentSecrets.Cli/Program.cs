using AgentSecrets;
using AgentSecrets.Cli;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("agent-secrets: this version supports Windows only (Windows Credential Manager backend).");
    return ExitCodes.Error;
}

var context = new CliContext
{
    Store = new WindowsCredentialStore(),
    Out = Console.Out,
    Error = Console.Error,
    In = Console.In,
    InputRedirected = Console.IsInputRedirected,
    ReadSecretInteractive = ConsoleSecretReader.Read,
    WorkingDirectory = Environment.CurrentDirectory,
};

return CliApp.Run(args, context);
