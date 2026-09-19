using System.Text;

namespace AgentSecrets;

/// <summary>
/// Builds the cmd.exe command line needed to run a .cmd/.bat file (npm, yarn, many tool shims).
/// Batch files are parsed by cmd.exe, whose quoting rules differ from normal programs; naive
/// quoting allows argument injection ("BatBadBut", CVE-2024-24576). This is a port of the
/// escaping used by the Rust standard library to fix that vulnerability.
/// </summary>
public static class BatchCommandLine
{
    private const string SafeUnquoted = @"#$*+-./:?@\_";

    /// <summary>Returns the arguments for cmd.exe (everything after the cmd.exe path).</summary>
    public static string Build(string scriptPath, IReadOnlyList<string> arguments)
    {
        if (scriptPath.Contains('"') || scriptPath.EndsWith('\\'))
        {
            throw new ArgumentException("The batch file path contains characters that cannot be passed to cmd.exe safely.");
        }

        // /e:ON is required for the '%' neutralization below; /v:OFF disables '!' expansion;
        // /d skips AutoRun commands. The whole command is wrapped in one extra pair of quotes.
        var commandLine = new StringBuilder("/e:ON /v:OFF /d /c \"\"").Append(scriptPath).Append('"');
        foreach (string argument in arguments)
        {
            commandLine.Append(' ');
            AppendArgument(commandLine, argument);
        }

        return commandLine.Append('"').ToString();
    }

    private static void AppendArgument(StringBuilder commandLine, string argument)
    {
        if (argument.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0)
        {
            throw new ArgumentException("Arguments containing line breaks or NUL cannot be passed to a .cmd/.bat file safely.");
        }

        // Quote everything that is not known to be harmless, plus empty arguments and
        // arguments ending in '\' (which would otherwise escape a closing quote).
        bool quote = argument.Length == 0 || argument.EndsWith('\\');
        foreach (char c in argument)
        {
            if (char.IsControl(c) || (char.IsAscii(c) && !char.IsAsciiLetterOrDigit(c) && !SafeUnquoted.Contains(c)))
            {
                quote = true;
                break;
            }
        }

        if (quote)
        {
            commandLine.Append('"');
        }

        int backslashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
            }
            else
            {
                if (c == '"')
                {
                    // 2n backslashes before an embedded quote, which is itself escaped by doubling.
                    commandLine.Append('\\', backslashes).Append('"');
                }
                else if (c == '%')
                {
                    // "%%cd:~,%" expands to an empty substring of %cd%, which stops cmd.exe
                    // from expanding %VARIABLE% references inside the argument.
                    commandLine.Append("%%cd:~,");
                }

                backslashes = 0;
            }

            commandLine.Append(c);
        }

        if (quote)
        {
            commandLine.Append('\\', backslashes).Append('"');
        }
    }
}
