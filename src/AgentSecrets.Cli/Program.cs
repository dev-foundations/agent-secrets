using AgentSecrets;
using AgentSecrets.Cli;

ISecretStore store;
if (OperatingSystem.IsWindows())
{
    store = new WindowsCredentialStore();
}
else if (OperatingSystem.IsMacOS())
{
    store = new MacKeychainStore();
}
else
{
    Console.Error.WriteLine("agent-secrets: this version supports Windows (Credential Manager) and macOS (Keychain) only.");
    return ExitCodes.Error;
}

var context = new CliContext
{
    Store = store,
    Out = Console.Out,
    Error = Console.Error,
    In = Console.In,
    InputRedirected = Console.IsInputRedirected,
    ReadSecretInteractive = ConsoleSecretReader.Read,
    WorkingDirectory = Environment.CurrentDirectory,
};

return CliApp.Run(args, context);
