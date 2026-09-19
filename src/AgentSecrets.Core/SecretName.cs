namespace AgentSecrets;

/// <summary>
/// Validation for secret aliases such as "openai". Aliases are case-insensitive
/// (Windows Credential Manager target names are) and normalized to lower case.
/// </summary>
public static class SecretName
{
    public const int MaxLength = 64;

    public const string Rules =
        "Use 1-64 characters: letters, digits, '.', '_' or '-', starting with a letter or digit.";

    public static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxLength || !char.IsAsciiLetterOrDigit(name[0]))
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Returns the canonical (lower-case) alias or throws <see cref="ArgumentException"/>.</summary>
    public static string Normalize(string? name)
    {
        if (!IsValid(name))
        {
            // The rejected text is not echoed: it could be a secret pasted in the wrong place.
            throw new ArgumentException($"Invalid secret name. {Rules}");
        }

        return name!.ToLowerInvariant();
    }
}
