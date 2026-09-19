namespace AgentSecrets;

/// <summary>
/// Environment-variable-to-alias bindings, e.g. OPENAI_API_KEY -> openai.
/// Variable names compare case-insensitively because Windows environment variables do.
/// </summary>
public static class Bindings
{
    public static Dictionary<string, string> Create() => new(StringComparer.OrdinalIgnoreCase);

    public static bool IsValidEnvName(string? name)
    {
        if (string.IsNullOrEmpty(name) || !(char.IsAsciiLetter(name[0]) || name[0] == '_'))
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Parses a command-line binding of the form NAME=alias.</summary>
    public static (string EnvName, string Alias) Parse(string text)
    {
        // Error messages never echo the right-hand side: someone may have put a real key there.
        int eq = text.IndexOf('=');
        if (eq <= 0 || eq == text.Length - 1)
        {
            throw new ArgumentException("Invalid binding. Expected ENV_NAME=secret-name, e.g. OPENAI_API_KEY=openai.");
        }

        string envName = text[..eq];
        if (!IsValidEnvName(envName))
        {
            throw new ArgumentException($"Invalid environment variable name '{envName}'. Use letters, digits and '_', not starting with a digit.");
        }

        string alias = text[(eq + 1)..];
        if (!SecretName.IsValid(alias))
        {
            throw new ArgumentException(
                $"Invalid secret name in the binding for {envName}. Expected the NAME of a stored secret (e.g. {envName}=openai), never the secret itself. {SecretName.Rules}");
        }

        return (envName, alias.ToLowerInvariant());
    }

    /// <summary>Manifest bindings first, then explicit bindings override them.</summary>
    public static Dictionary<string, string> Merge(
        IReadOnlyDictionary<string, string>? manifest,
        IEnumerable<KeyValuePair<string, string>> explicitBindings)
    {
        var merged = Create();
        if (manifest is not null)
        {
            foreach (var (envName, alias) in manifest)
            {
                merged[envName] = alias;
            }
        }

        foreach (var (envName, alias) in explicitBindings)
        {
            // Remove first so the explicit spelling of the variable name wins too.
            merged.Remove(envName);
            merged[envName] = alias;
        }

        return merged;
    }
}
