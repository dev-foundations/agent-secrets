using System.Text;

namespace AgentSecrets.Cli;

public static class ConsoleSecretReader
{
    /// <summary>Reads one line from the terminal without echoing anything (typed or pasted).</summary>
    public static string Read()
    {
        var buffer = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }

        string value = buffer.ToString();
        buffer.Clear();
        return value;
    }
}
